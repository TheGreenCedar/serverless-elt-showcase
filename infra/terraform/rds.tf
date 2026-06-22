locals {
  db_name            = "fuelmix"
  db_port            = 5432
  writer_db_username = "fuelmix_writer"
  read_db_username   = "fuelmix_reader"
}

resource "aws_security_group" "lambda_clients" {
  name        = "${var.project_name}-lambda-clients"
  description = "Lambda clients that can reach only approved private dependencies"
  vpc_id      = var.vpc_id
}

resource "aws_security_group" "rds_proxy" {
  name        = "${var.project_name}-rds-proxy"
  description = "RDS Proxy accepts PostgreSQL from Lambda clients"
  vpc_id      = var.vpc_id
}

resource "aws_security_group" "postgres" {
  name        = "${var.project_name}-postgres"
  description = "Private PostgreSQL accepts traffic only from RDS Proxy"
  vpc_id      = var.vpc_id
}

resource "aws_security_group" "secretsmanager_endpoint" {
  name        = "${var.project_name}-secretsmanager-endpoint"
  description = "Secrets Manager endpoint accepts HTTPS from Lambda clients"
  vpc_id      = var.vpc_id
}

resource "aws_security_group_rule" "lambda_to_proxy" {
  type                     = "egress"
  description              = "PostgreSQL through RDS Proxy only"
  from_port                = local.db_port
  to_port                  = local.db_port
  protocol                 = "tcp"
  security_group_id        = aws_security_group.lambda_clients.id
  source_security_group_id = aws_security_group.rds_proxy.id
}

resource "aws_security_group_rule" "lambda_to_secretsmanager_endpoint" {
  type                     = "egress"
  description              = "HTTPS to private Secrets Manager endpoint"
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  security_group_id        = aws_security_group.lambda_clients.id
  source_security_group_id = aws_security_group.secretsmanager_endpoint.id
}

resource "aws_security_group_rule" "secretsmanager_endpoint_from_lambda" {
  type                     = "ingress"
  description              = "HTTPS from Lambda clients"
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  security_group_id        = aws_security_group.secretsmanager_endpoint.id
  source_security_group_id = aws_security_group.lambda_clients.id
}

resource "aws_security_group_rule" "proxy_from_lambda" {
  type                     = "ingress"
  description              = "PostgreSQL from Lambda clients"
  from_port                = local.db_port
  to_port                  = local.db_port
  protocol                 = "tcp"
  security_group_id        = aws_security_group.rds_proxy.id
  source_security_group_id = aws_security_group.lambda_clients.id
}

resource "aws_security_group_rule" "proxy_to_postgres" {
  type                     = "egress"
  description              = "PostgreSQL to private RDS instance"
  from_port                = local.db_port
  to_port                  = local.db_port
  protocol                 = "tcp"
  security_group_id        = aws_security_group.rds_proxy.id
  source_security_group_id = aws_security_group.postgres.id
}

resource "aws_security_group_rule" "postgres_from_proxy" {
  type                     = "ingress"
  description              = "PostgreSQL from RDS Proxy"
  from_port                = local.db_port
  to_port                  = local.db_port
  protocol                 = "tcp"
  security_group_id        = aws_security_group.postgres.id
  source_security_group_id = aws_security_group.rds_proxy.id
}

resource "aws_vpc_endpoint" "secretsmanager" {
  private_dns_enabled = true
  security_group_ids  = [aws_security_group.secretsmanager_endpoint.id]
  service_name        = "com.amazonaws.${var.aws_region}.secretsmanager"
  subnet_ids          = var.private_subnet_ids
  vpc_endpoint_type   = "Interface"
  vpc_id              = var.vpc_id

  depends_on = [
    aws_security_group_rule.secretsmanager_endpoint_from_lambda
  ]
}

resource "aws_db_subnet_group" "postgres" {
  name       = "${var.project_name}-db-subnets"
  subnet_ids = var.private_subnet_ids
}

resource "aws_secretsmanager_secret" "db_admin" {
  name = "${var.project_name}-db-admin"
}

resource "aws_secretsmanager_secret_version" "db_admin" {
  secret_id = aws_secretsmanager_secret.db_admin.id

  secret_string = jsonencode({
    username = var.db_admin_username
    password = var.db_admin_password
  })
}

resource "aws_secretsmanager_secret" "writer_db" {
  name = "${var.project_name}-writer-db"
}

resource "aws_secretsmanager_secret_version" "writer_db" {
  secret_id = aws_secretsmanager_secret.writer_db.id

  secret_string = jsonencode({
    username = local.writer_db_username
    password = var.writer_db_password
  })
}

resource "aws_secretsmanager_secret" "read_db" {
  name = "${var.project_name}-read-db"
}

resource "aws_secretsmanager_secret_version" "read_db" {
  secret_id = aws_secretsmanager_secret.read_db.id

  secret_string = jsonencode({
    username = local.read_db_username
    password = var.read_db_password
  })
}

resource "aws_db_instance" "postgres" {
  identifier             = "${var.project_name}-postgres"
  engine                 = "postgres"
  engine_version         = "16"
  instance_class         = "db.t4g.micro"
  allocated_storage      = 20
  db_name                = local.db_name
  username               = var.db_admin_username
  password               = var.db_admin_password
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.postgres.id]
  publicly_accessible    = false
  skip_final_snapshot    = true
  storage_encrypted      = true
}

data "aws_iam_policy_document" "rds_proxy_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["rds.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "rds_proxy" {
  name               = "${var.project_name}-rds-proxy"
  assume_role_policy = data.aws_iam_policy_document.rds_proxy_assume.json
}

resource "aws_iam_role_policy" "rds_proxy_secret" {
  name = "${var.project_name}-rds-proxy-secret"
  role = aws_iam_role.rds_proxy.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = ["secretsmanager:GetSecretValue"]
        Resource = [
          aws_secretsmanager_secret.writer_db.arn,
          aws_secretsmanager_secret.read_db.arn
        ]
      }
    ]
  })
}

resource "aws_db_proxy" "postgres" {
  name                   = "${var.project_name}-postgres-proxy"
  engine_family          = "POSTGRESQL"
  idle_client_timeout    = 1800
  require_tls            = true
  role_arn               = aws_iam_role.rds_proxy.arn
  vpc_security_group_ids = [aws_security_group.rds_proxy.id]
  vpc_subnet_ids         = var.private_subnet_ids

  auth {
    auth_scheme = "SECRETS"
    iam_auth    = "DISABLED"
    secret_arn  = aws_secretsmanager_secret.writer_db.arn
  }

  auth {
    auth_scheme = "SECRETS"
    iam_auth    = "DISABLED"
    secret_arn  = aws_secretsmanager_secret.read_db.arn
  }

  depends_on = [
    aws_iam_role_policy.rds_proxy_secret,
    aws_secretsmanager_secret_version.read_db,
    aws_secretsmanager_secret_version.writer_db,
    aws_security_group_rule.proxy_from_lambda,
    aws_security_group_rule.proxy_to_postgres
  ]
}

resource "aws_db_proxy_default_target_group" "postgres" {
  db_proxy_name = aws_db_proxy.postgres.name

  connection_pool_config {
    connection_borrow_timeout    = 30
    max_connections_percent      = 70
    max_idle_connections_percent = 50
  }
}

resource "aws_db_proxy_target" "postgres" {
  db_instance_identifier = aws_db_instance.postgres.identifier
  db_proxy_name          = aws_db_proxy.postgres.name
  target_group_name      = aws_db_proxy_default_target_group.postgres.name

  depends_on = [
    aws_db_proxy_default_target_group.postgres
  ]
}
