resource "aws_cloudwatch_metric_alarm" "dlq_visible_messages" {
  alarm_name          = "${var.project_name}-dlq-visible-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    QueueName = aws_sqs_queue.raw_snapshot_dlq.name
  }
}

resource "aws_cloudwatch_metric_alarm" "scheduler_dlq_visible_messages" {
  alarm_name          = "${var.project_name}-scheduler-dlq-visible-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    QueueName = aws_sqs_queue.scheduled_fetch_dlq.name
  }
}

resource "aws_cloudwatch_metric_alarm" "fetch_async_failure_dlq_visible_messages" {
  alarm_name          = "${var.project_name}-fetch-async-failure-dlq-visible-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    QueueName = aws_sqs_queue.fetch_async_failure_dlq.name
  }
}

resource "aws_cloudwatch_metric_alarm" "raw_queue_oldest_message_age" {
  alarm_name          = "${var.project_name}-raw-queue-oldest-message-age"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateAgeOfOldestMessage"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 180
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    QueueName = aws_sqs_queue.raw_snapshot.name
  }
}

resource "aws_cloudwatch_metric_alarm" "raw_queue_visible_messages" {
  alarm_name          = "${var.project_name}-raw-queue-visible-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 10
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    QueueName = aws_sqs_queue.raw_snapshot.name
  }
}

resource "aws_cloudwatch_metric_alarm" "fetch_errors" {
  alarm_name          = "${var.project_name}-fetch-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    FunctionName = aws_lambda_function.fetch.function_name
  }
}

resource "aws_cloudwatch_metric_alarm" "writer_errors" {
  alarm_name          = "${var.project_name}-writer-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    FunctionName = aws_lambda_function.writer.function_name
  }
}

resource "aws_cloudwatch_metric_alarm" "fuelmix_fetch_failed" {
  alarm_name          = "${var.project_name}-fuelmix-fetch-failed"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "FuelMixFetchFailed"
  namespace           = "TecFuelMix"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    service = "FetchLambda"
  }
}

resource "aws_cloudwatch_metric_alarm" "fuelmix_write_failed" {
  alarm_name          = "${var.project_name}-fuelmix-write-failed"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "FuelMixWriteFailed"
  namespace           = "TecFuelMix"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    service = "WriterLambda"
  }
}

resource "aws_cloudwatch_metric_alarm" "fuelmix_partial_batch_failures" {
  alarm_name          = "${var.project_name}-fuelmix-partial-batch-failures"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "FuelMixPartialBatchFailures"
  namespace           = "TecFuelMix"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    service = "WriterLambda"
  }
}

resource "aws_cloudwatch_metric_alarm" "fuelmix_write_succeeded_stale" {
  alarm_name          = "${var.project_name}-fuelmix-write-succeeded-stale"
  comparison_operator = "LessThanThreshold"
  evaluation_periods  = 1
  metric_name         = "FuelMixWriteSucceeded"
  namespace           = "TecFuelMix"
  period              = 300
  statistic           = "Sum"
  threshold           = 1
  treat_missing_data  = "breaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    service = "WriterLambda"
  }
}

resource "aws_cloudwatch_metric_alarm" "read_api_throttles" {
  alarm_name          = "${var.project_name}-read-api-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Throttles"
  namespace           = "AWS/Lambda"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    FunctionName = aws_lambda_function.read_api.function_name
  }
}

resource "aws_cloudwatch_metric_alarm" "read_api_5xx_errors" {
  alarm_name          = "${var.project_name}-read-api-5xx-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "5XXError"
  namespace           = "AWS/ApiGateway"
  period              = 60
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    ApiName = aws_api_gateway_rest_api.read_api.name
    Stage   = aws_api_gateway_stage.prod.stage_name
  }
}

resource "aws_cloudwatch_metric_alarm" "read_api_latency_p95" {
  alarm_name          = "${var.project_name}-read-api-latency-p95"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  extended_statistic  = "p95"
  metric_name         = "Latency"
  namespace           = "AWS/ApiGateway"
  period              = 60
  threshold           = 1000
  treat_missing_data  = "notBreaching"
  unit                = "Milliseconds"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    ApiName = aws_api_gateway_rest_api.read_api.name
    Stage   = aws_api_gateway_stage.prod.stage_name
  }
}

resource "aws_cloudwatch_metric_alarm" "postgres_cpu_utilization" {
  alarm_name          = "${var.project_name}-postgres-cpu-utilization"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "CPUUtilization"
  namespace           = "AWS/RDS"
  period              = 300
  statistic           = "Average"
  threshold           = 80
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    DBInstanceIdentifier = aws_db_instance.postgres.identifier
  }
}

resource "aws_cloudwatch_metric_alarm" "postgres_free_storage_space" {
  alarm_name          = "${var.project_name}-postgres-free-storage-space"
  comparison_operator = "LessThanThreshold"
  evaluation_periods  = 2
  metric_name         = "FreeStorageSpace"
  namespace           = "AWS/RDS"
  period              = 300
  statistic           = "Average"
  threshold           = 2147483648
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    DBInstanceIdentifier = aws_db_instance.postgres.identifier
  }
}

resource "aws_cloudwatch_metric_alarm" "postgres_database_connections" {
  alarm_name          = "${var.project_name}-postgres-database-connections"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "DatabaseConnections"
  namespace           = "AWS/RDS"
  period              = 300
  statistic           = "Average"
  threshold           = 60
  treat_missing_data  = "notBreaching"
  alarm_actions       = var.alarm_action_arns
  ok_actions          = var.alarm_action_arns

  dimensions = {
    DBInstanceIdentifier = aws_db_instance.postgres.identifier
  }
}
