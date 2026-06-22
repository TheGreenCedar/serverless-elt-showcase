locals {
  writer_postgres_connection_string = "Host=${aws_db_proxy.postgres.endpoint};Port=${local.db_port};Database=${local.db_name};Username=${var.writer_db_username};Password=${var.writer_db_password};SSL Mode=Require"
  read_postgres_connection_string   = "Host=${aws_db_proxy.postgres.endpoint};Port=${local.db_port};Database=${local.db_name};Username=${var.read_db_username};Password=${var.read_db_password};SSL Mode=Require"
}

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "fetch_lambda" {
  name               = "${var.project_name}-fetch-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role" "writer_lambda" {
  name               = "${var.project_name}-writer-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role" "read_api_lambda" {
  name               = "${var.project_name}-read-api-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role_policy_attachment" "fetch_lambda_basic" {
  role       = aws_iam_role.fetch_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy_attachment" "writer_lambda_basic" {
  role       = aws_iam_role.writer_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy_attachment" "writer_lambda_vpc" {
  role       = aws_iam_role.writer_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy_attachment" "read_api_lambda_basic" {
  role       = aws_iam_role.read_api_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy_attachment" "read_api_lambda_vpc" {
  role       = aws_iam_role.read_api_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "fetch_sqs" {
  name = "${var.project_name}-fetch-sqs"
  role = aws_iam_role.fetch_lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["sqs:SendMessage"]
        Resource = aws_sqs_queue.raw_snapshot.arn
      }
    ]
  })
}

resource "aws_iam_role_policy" "writer_sqs" {
  name = "${var.project_name}-writer-sqs"
  role = aws_iam_role.writer_lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:GetQueueAttributes",
          "sqs:ChangeMessageVisibility"
        ]
        Resource = aws_sqs_queue.raw_snapshot.arn
      }
    ]
  })
}

resource "aws_lambda_function" "fetch" {
  function_name                  = "${var.project_name}-fetch"
  role                           = aws_iam_role.fetch_lambda.arn
  package_type                   = "Image"
  image_uri                      = var.fetch_lambda_image_uri
  timeout                        = 30
  reserved_concurrent_executions = 1

  environment {
    variables = {
      RAW_SNAPSHOT_QUEUE_URL = aws_sqs_queue.raw_snapshot.url
    }
  }
}

resource "aws_lambda_function" "writer" {
  function_name                  = "${var.project_name}-writer"
  role                           = aws_iam_role.writer_lambda.arn
  package_type                   = "Image"
  image_uri                      = var.writer_lambda_image_uri
  timeout                        = 60
  reserved_concurrent_executions = 1

  vpc_config {
    security_group_ids = [aws_security_group.lambda_clients.id]
    subnet_ids         = var.private_subnet_ids
  }

  environment {
    variables = {
      POSTGRES_CONNECTION_STRING = local.writer_postgres_connection_string
    }
  }
}

resource "aws_lambda_function" "read_api" {
  function_name                  = "${var.project_name}-read-api"
  role                           = aws_iam_role.read_api_lambda.arn
  package_type                   = "Image"
  image_uri                      = var.read_api_lambda_image_uri
  timeout                        = 30
  reserved_concurrent_executions = var.read_api_reserved_concurrency

  vpc_config {
    security_group_ids = [aws_security_group.lambda_clients.id]
    subnet_ids         = var.private_subnet_ids
  }

  environment {
    variables = {
      POSTGRES_CONNECTION_STRING = local.read_postgres_connection_string
    }
  }
}

resource "aws_lambda_event_source_mapping" "writer_from_sqs" {
  event_source_arn        = aws_sqs_queue.raw_snapshot.arn
  function_name           = aws_lambda_function.writer.arn
  batch_size              = 5
  function_response_types = ["ReportBatchItemFailures"]
}

data "aws_iam_policy_document" "scheduler_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["scheduler.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "scheduler" {
  name               = "${var.project_name}-scheduler"
  assume_role_policy = data.aws_iam_policy_document.scheduler_assume.json
}

resource "aws_iam_role_policy" "scheduler_invoke" {
  name = "${var.project_name}-scheduler-invoke"
  role = aws_iam_role.scheduler.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["lambda:InvokeFunction"]
        Resource = aws_lambda_function.fetch.arn
      },
      {
        Effect   = "Allow"
        Action   = ["sqs:SendMessage"]
        Resource = aws_sqs_queue.scheduled_fetch_dlq.arn
      }
    ]
  })
}

resource "aws_scheduler_schedule" "fetch" {
  name                = "${var.project_name}-fetch"
  schedule_expression = "rate(1 minute)"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.fetch.arn
    role_arn = aws_iam_role.scheduler.arn

    dead_letter_config {
      arn = aws_sqs_queue.scheduled_fetch_dlq.arn
    }

    retry_policy {
      maximum_event_age_in_seconds = 300
      maximum_retry_attempts       = 0
    }
  }
}
