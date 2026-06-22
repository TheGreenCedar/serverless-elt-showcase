variable "aws_region" {
  description = "AWS region for all resources."
  type        = string
  default     = "us-east-1"
}

variable "project_name" {
  description = "Short project prefix used in AWS resource names."
  type        = string
  default     = "tec-fuelmix"
}

variable "vpc_id" {
  description = "VPC that contains the private application and database subnets."
  type        = string
}

variable "private_subnet_ids" {
  description = "Private subnet IDs for RDS, RDS Proxy, and database Lambda ENIs."
  type        = list(string)

  validation {
    condition     = length(var.private_subnet_ids) >= 2
    error_message = "At least two private subnets are required for RDS and RDS Proxy availability."
  }
}

variable "db_admin_username" {
  description = "Initial PostgreSQL admin username for RDS instance creation."
  type        = string
  default     = "fuelmix_admin"
}

variable "db_admin_password" {
  description = "Initial PostgreSQL admin password for RDS instance creation."
  type        = string
  sensitive   = true
}

variable "writer_db_username" {
  description = "PostgreSQL writer username. The role and grants must be bootstrapped outside this Terraform-only slice."
  type        = string
  default     = "fuelmix_writer"
}

variable "writer_db_password" {
  description = "PostgreSQL writer password stored for RDS Proxy auth."
  type        = string
  sensitive   = true
}

variable "read_db_username" {
  description = "PostgreSQL read-only username. The role and grants must be bootstrapped outside this Terraform-only slice."
  type        = string
  default     = "fuelmix_reader"
}

variable "read_db_password" {
  description = "PostgreSQL read-only password stored for RDS Proxy auth."
  type        = string
  sensitive   = true
}

variable "fetch_lambda_image_uri" {
  description = "Container image URI for the scheduled fetch Lambda."
  type        = string
}

variable "writer_lambda_image_uri" {
  description = "Container image URI for the SQS writer Lambda."
  type        = string
}

variable "read_api_lambda_image_uri" {
  description = "Container image URI for the read API Lambda."
  type        = string
}

variable "read_api_cache_ttl_seconds" {
  description = "API Gateway method cache TTL for shared read responses."
  type        = number
  default     = 30
}

variable "read_api_reserved_concurrency" {
  description = "Reserved concurrency cap for the read API Lambda as a DB protection backstop."
  type        = number
  default     = 25
}

variable "alarm_action_arns" {
  description = "Optional SNS topic or incident action ARNs used for CloudWatch alarm and recovery notifications."
  type        = list(string)
  default     = []
}
