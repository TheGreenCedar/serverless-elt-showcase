resource "aws_sqs_queue" "raw_snapshot_dlq" {
  name = "${var.project_name}-raw-snapshot-dlq"
}

resource "aws_sqs_queue" "raw_snapshot" {
  name                       = "${var.project_name}-raw-snapshot"
  visibility_timeout_seconds = 360

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.raw_snapshot_dlq.arn
    maxReceiveCount     = 5
  })
}

resource "aws_sqs_queue" "scheduled_fetch_dlq" {
  name = "${var.project_name}-scheduled-fetch-dlq"
}

resource "aws_sqs_queue" "fetch_async_failure_dlq" {
  name = "${var.project_name}-fetch-async-failure-dlq"
}
