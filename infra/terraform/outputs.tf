output "raw_snapshot_queue_url" {
  description = "SQS queue URL used by the fetch Lambda."
  value       = aws_sqs_queue.raw_snapshot.url
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
