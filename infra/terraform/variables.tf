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

variable "writer_db_password" {
  description = "PostgreSQL writer password stored for RDS Proxy auth."
  type        = string
  sensitive   = true
}

variable "read_db_password" {
  description = "PostgreSQL read-only password stored for RDS Proxy auth."
  type        = string
  sensitive   = true
}

variable "rds_deletion_protection" {
  description = "Whether the RDS instance rejects deletion. Keep true for production-shaped deployments; set false before challenge teardown."
  type        = bool
  default     = true
}

variable "rds_skip_final_snapshot" {
  description = "Whether RDS deletion skips the final snapshot. Keep false for production-shaped deployments; set true only for disposable challenge teardown."
  type        = bool
  default     = false
}

variable "rds_final_snapshot_identifier" {
  description = "Final snapshot identifier used when rds_skip_final_snapshot is false. Defaults to '<project_name>-postgres-final' when omitted."
  type        = string
  default     = null

  validation {
    condition     = var.rds_final_snapshot_identifier == null || length(var.rds_final_snapshot_identifier) > 0
    error_message = "rds_final_snapshot_identifier must be null or a non-empty string."
  }
}

variable "rds_backup_retention_days" {
  description = "Automated RDS backup retention in days. Use 1-35 for production-shaped deployments; 0 disables automated backups."
  type        = number
  default     = 7

  validation {
    condition     = var.rds_backup_retention_days >= 0 && var.rds_backup_retention_days <= 35
    error_message = "rds_backup_retention_days must be between 0 and 35."
  }
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

variable "migrator_lambda_image_uri" {
  description = "Container image URI for the one-shot database migrator Lambda."
  type        = string
}

variable "read_api_bearer_token" {
  description = "Bearer token accepted by the read API Lambda authorizer."
  type        = string
  sensitive   = true
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
