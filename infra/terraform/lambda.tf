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

resource "aws_iam_role" "read_api_authorizer_lambda" {
  name               = "${var.project_name}-read-api-authorizer-lambda"
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

resource "aws_iam_role_policy_attachment" "read_api_authorizer_lambda_basic" {
  role       = aws_iam_role.read_api_authorizer_lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
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

resource "aws_iam_role_policy" "fetch_async_failure_destination" {
  name = "${var.project_name}-fetch-async-failure-destination"
  role = aws_iam_role.fetch_lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["sqs:SendMessage"]
        Resource = aws_sqs_queue.fetch_async_failure_dlq.arn
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

resource "aws_iam_role_policy" "writer_db_secret" {
  name = "${var.project_name}-writer-db-secret"
  role = aws_iam_role.writer_lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = aws_secretsmanager_secret.writer_db.arn
      }
    ]
  })
}

resource "aws_iam_role_policy" "read_api_db_secret" {
  name = "${var.project_name}-read-api-db-secret"
  role = aws_iam_role.read_api_lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = aws_secretsmanager_secret.read_db.arn
      }
    ]
  })
}

resource "aws_cloudwatch_log_group" "fetch" {
  name              = "/aws/lambda/${var.project_name}-fetch"
  retention_in_days = 14
}

resource "aws_cloudwatch_log_group" "writer" {
  name              = "/aws/lambda/${var.project_name}-writer"
  retention_in_days = 14
}

resource "aws_cloudwatch_log_group" "read_api" {
  name              = "/aws/lambda/${var.project_name}-read-api"
  retention_in_days = 14
}

resource "aws_cloudwatch_log_group" "read_api_authorizer" {
  name              = "/aws/lambda/${var.project_name}-read-api-authorizer"
  retention_in_days = 14
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

  depends_on = [
    aws_cloudwatch_log_group.fetch,
    aws_iam_role_policy_attachment.fetch_lambda_basic,
    aws_iam_role_policy.fetch_async_failure_destination,
    aws_iam_role_policy.fetch_sqs
  ]
}

resource "aws_lambda_function_event_invoke_config" "fetch" {
  function_name = aws_lambda_function.fetch.function_name

  maximum_event_age_in_seconds = 60
  maximum_retry_attempts       = 0

  destination_config {
    on_failure {
      destination = aws_sqs_queue.fetch_async_failure_dlq.arn
    }
  }

  depends_on = [
    aws_iam_role_policy.fetch_async_failure_destination
  ]
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
      POSTGRES_DATABASE   = local.db_name
      POSTGRES_HOST       = aws_db_proxy.postgres.endpoint
      POSTGRES_SECRET_ARN = aws_secretsmanager_secret.writer_db.arn
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.writer,
    aws_iam_role_policy.writer_db_secret,
    aws_iam_role_policy.writer_sqs,
    aws_iam_role_policy_attachment.writer_lambda_basic,
    aws_iam_role_policy_attachment.writer_lambda_vpc,
    aws_vpc_endpoint.secretsmanager
  ]
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
      POSTGRES_DATABASE   = local.db_name
      POSTGRES_HOST       = aws_db_proxy.postgres.endpoint
      POSTGRES_SECRET_ARN = aws_secretsmanager_secret.read_db.arn
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.read_api,
    aws_iam_role_policy.read_api_db_secret,
    aws_iam_role_policy_attachment.read_api_lambda_basic,
    aws_iam_role_policy_attachment.read_api_lambda_vpc,
    aws_vpc_endpoint.secretsmanager
  ]
}

resource "aws_lambda_function" "read_api_authorizer" {
  function_name = "${var.project_name}-read-api-authorizer"
  role          = aws_iam_role.read_api_authorizer_lambda.arn
  package_type  = "Image"
  image_uri     = var.read_api_lambda_image_uri
  timeout       = 10

  image_config {
    command = ["TecFuelMix.ReadApiLambda::TecFuelMix.ReadApiLambda.Function::Authorize"]
  }

  environment {
    variables = {
      READ_API_BEARER_TOKEN = var.read_api_bearer_token
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.read_api_authorizer,
    aws_iam_role_policy_attachment.read_api_authorizer_lambda_basic
  ]
}

resource "aws_lambda_event_source_mapping" "writer_from_sqs" {
  event_source_arn        = aws_sqs_queue.raw_snapshot.arn
  function_name           = aws_lambda_function.writer.arn
  batch_size              = 5
  function_response_types = ["ReportBatchItemFailures"]

  depends_on = [
    aws_iam_role_policy.writer_sqs
  ]
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

  depends_on = [
    aws_iam_role_policy.scheduler_invoke
  ]
}
