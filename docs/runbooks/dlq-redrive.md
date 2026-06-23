# SQS DLQ Redrive Runbook

Use this when messages appear in the raw snapshot DLQ: `tec-fuelmix-raw-snapshot-dlq`.

The raw snapshot queue stores MISO payloads already fetched by `TecFuelMix.FetchLambda`. Redriving this DLQ replays those stored payloads through the writer. It does not call MISO again and does not create another source poll.

## Before Redrive

Confirm which queue has messages:

```bash
region='us-east-1'
raw_dlq_url="$(aws sqs get-queue-url --region "$region" --queue-name tec-fuelmix-raw-snapshot-dlq --query QueueUrl --output text)"
aws sqs get-queue-attributes --region "$region" --queue-url "$raw_dlq_url" --attribute-names ApproximateNumberOfMessages ApproximateAgeOfOldestMessage
```

Inspect failed messages without deleting them:

```bash
aws sqs receive-message --region "$region" --queue-url "$raw_dlq_url" --max-number-of-messages 1 --visibility-timeout 30 --message-attribute-names All --attribute-names All
```

Look for:

- `source_ref_id` message attribute, when present.
- `fetched_at_utc` message attribute, when present.
- Body shape: it should be the raw MISO FuelMix JSON payload.
- Writer Lambda logs for the same message ID or source ref.

## Do Not Redrive Yet When

- The writer is still failing on current traffic.
- PostgreSQL, RDS Proxy, Secrets Manager, or the private VPC endpoint is unavailable.
- The failure was a parser/schema bug and the fix has not been deployed.
- The message body is not a raw MISO FuelMix payload.
- The DLQ is growing and the root cause is still unknown.

Fix the failing layer first. Redrive is replay, not repair.

## Why Replay Is Safe

- Redrive moves existing SQS messages back to the source queue.
- The writer parses `record.Body`; it does not call the MISO API.
- PostgreSQL unique keys in `src/TecFuelMix.Core/Migrations/001_schema.sql` enforce one snapshot per `source_ref_id` and one reading per snapshot/category.
- Duplicate SQS delivery should update the existing snapshot path instead of creating duplicate fuel-mix readings.

## Redrive

Use the AWS console redrive action for `tec-fuelmix-raw-snapshot-dlq`, or start a CLI redrive task from the DLQ ARN to the source queue ARN.

Get the ARNs:

```bash
raw_queue_url="$(terraform -chdir=infra/terraform output -raw raw_snapshot_queue_url)"
raw_queue_arn="$(aws sqs get-queue-attributes --region "$region" --queue-url "$raw_queue_url" --attribute-names QueueArn --query 'Attributes.QueueArn' --output text)"
raw_dlq_arn="$(aws sqs get-queue-attributes --region "$region" --queue-url "$raw_dlq_url" --attribute-names QueueArn --query 'Attributes.QueueArn' --output text)"
```

Start the redrive task:

```bash
aws sqs start-message-move-task --region "$region" --source-arn "$raw_dlq_arn" --destination-arn "$raw_queue_arn"
```

Monitor the source queue, DLQ, and writer logs:

```bash
aws sqs get-queue-attributes --region "$region" --queue-url "$raw_queue_url" --attribute-names ApproximateNumberOfMessages ApproximateNumberOfMessagesNotVisible ApproximateAgeOfOldestMessage
aws sqs get-queue-attributes --region "$region" --queue-url "$raw_dlq_url" --attribute-names ApproximateNumberOfMessages
aws logs tail /aws/lambda/tec-fuelmix-writer --region "$region" --since 15m
```

## After Redrive

Check that the read path can see a current snapshot:

```bash
read_url="$(terraform -chdir=infra/terraform output -raw read_api_invoke_url)"
api_key="$(terraform -chdir=infra/terraform output -raw read_api_key_value)"
bearer='<same-value-as-terraform-read_api_bearer_token>'

curl --fail --show-error --silent \
  --header "x-api-key: $api_key" \
  --header "Authorization: Bearer $bearer" \
  "$read_url"
```

Capture evidence only when these commands were run against AWS:

- DLQ attributes before redrive.
- One inspected message with secrets and raw payload redacted as needed.
- Redrive task command or console task ID.
- Source queue and DLQ attributes after redrive.
- Writer log excerpt showing successful persistence.
- Read API smoke result.

If redrive fails again, leave the messages in the DLQ, stop retries, and keep the latest message sample plus writer error type for debugging.
