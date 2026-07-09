output "raw_snapshot_queue_url" {
  description = "SQS queue URL used by the fetch Lambda."
  value       = aws_sqs_queue.raw_snapshot.url
}

output "fetch_ecr_repository_url" {
  description = "ECR repository URL for the fetch Lambda image."
  value       = aws_ecr_repository.fetch.repository_url
}

output "writer_ecr_repository_url" {
  description = "ECR repository URL for the writer Lambda image."
  value       = aws_ecr_repository.writer.repository_url
}

output "read_api_ecr_repository_url" {
  description = "ECR repository URL for the read API Lambda image."
  value       = aws_ecr_repository.read_api.repository_url
}

output "migrator_ecr_repository_url" {
  description = "ECR repository URL for the migrator Lambda image."
  value       = aws_ecr_repository.migrator.repository_url
}

output "rds_proxy_endpoint" {
  description = "Private RDS Proxy endpoint used by writer and read Lambdas."
  value       = aws_db_proxy.postgres.endpoint
}

output "read_api_id" {
  description = "API Gateway REST API ID for the cached read API."
  value       = aws_api_gateway_rest_api.read_api.id
}

output "read_api_invoke_url" {
  description = "Prod stage invoke URL for the cached read API."
  value       = "${aws_api_gateway_stage.prod.invoke_url}/fuel-mix/latest"
}

output "read_api_key_value" {
  description = "Generated API key value for external read clients."
  value       = aws_api_gateway_api_key.external_reader.value
  sensitive   = true
}

output "read_api_authorizer_lambda_name" {
  description = "Lambda function name used by API Gateway token authorizer."
  value       = aws_lambda_function.read_api_authorizer.function_name
}

output "migrator_lambda_name" {
  description = "One-shot Lambda function name used for database migrations."
  value       = aws_lambda_function.migrator.function_name
}

output "writer_db_secret_arn" {
  description = "Secrets Manager secret ARN used by the writer Lambda."
  value       = aws_secretsmanager_secret.writer_db.arn
}

output "read_db_secret_arn" {
  description = "Secrets Manager secret ARN used by the read API Lambda."
  value       = aws_secretsmanager_secret.read_db.arn
}
