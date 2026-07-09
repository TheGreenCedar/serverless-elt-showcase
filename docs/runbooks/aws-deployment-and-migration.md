# AWS Deployment And Migration Runbook

Use this to deploy a new TEC Fuel Mix environment into AWS. The primary path is the repo-owned script; the manual details explain what it does and how to recover.

## Preconditions

- AWS CLI is authenticated to the target account and region.
- Docker can build Linux Lambda images.
- Terraform `>= 1.7.0` is available.
- `infra/terraform/terraform.tfvars` exists with real values for `vpc_id`, `private_subnet_ids`, `db_admin_password`, `writer_db_password`, `read_db_password`, and `read_api_bearer_token`.
- `READ_API_BEARER_TOKEN` is set in the environment unless smoke checks are skipped.
- Do not commit `terraform.tfvars`, Terraform state, API keys, bearer tokens, or unredacted secret output.

## One Command

From the repository root:

```powershell
$env:READ_API_BEARER_TOKEN='<same-value-as-read_api_bearer_token>'
.\scripts\deploy-aws.ps1
```

The script:

1. derives an image tag from the current commit unless `-Tag` is supplied;
2. bootstraps the four ECR repositories with Terraform targets;
3. builds and pushes fetch, writer, read API, and migrator Lambda images;
4. runs `terraform fmt -check`, `terraform validate`, `terraform plan`, and `terraform apply`;
5. invokes the migration Lambda inside the VPC path and fails if the invoke metadata reports `FunctionError`;
6. invokes the fetch Lambda once and fails if the invoke metadata reports `FunctionError`;
7. checks raw snapshot queue attributes;
8. calls the latest read API route with the API key and bearer token;
9. writes real command evidence under ignored `docs/evidence/aws-*` paths.

Useful switches:

```powershell
.\scripts\deploy-aws.ps1 -PlanOnly
.\scripts\deploy-aws.ps1 -SkipSmoke
.\scripts\deploy-aws.ps1 -Region us-east-1 -ProjectName tec-fuelmix -Tag '<release-or-commit-sha>'
```

ECR tags are immutable. The default tag is the current commit short SHA; use `-Tag` with a new value if you intentionally need to push a second image build from the same commit.

## Migration Path

Terraform creates `aws_lambda_function.migrator`. The deploy script invokes it after `terraform apply`.

The migration Lambda:

- runs in the same private subnets and Lambda client security group as the writer/read Lambdas;
- connects through RDS Proxy using the admin secret;
- reads writer/read role passwords from Secrets Manager;
- runs the embedded DbUp scripts from `src/TecFuelMix.Core/Migrations`;
- updates `fuelmix_writer` and `fuelmix_reader` login passwords.

The local console migrator remains useful for local Docker PostgreSQL and break-glass diagnostics:

```powershell
$env:POSTGRES_ADMIN_CONNECTION_STRING='Host=<rds-proxy-or-local-db>;Port=5432;Database=fuelmix;Username=fuelmix_admin;Password=<admin-password>;SSL Mode=Require'
$env:WRITER_DB_PASSWORD='<writer-db-password>'
$env:READ_DB_PASSWORD='<reader-db-password>'
dotnet run --project .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj
```

POSIX shell equivalent:

```bash
export POSTGRES_ADMIN_CONNECTION_STRING='Host=<rds-proxy-or-local-db>;Port=5432;Database=fuelmix;Username=fuelmix_admin;Password=<admin-password>;SSL Mode=Require'
export WRITER_DB_PASSWORD='<writer-db-password>'
export READ_DB_PASSWORD='<reader-db-password>'
dotnet run --project ./src/TecFuelMix.DbMigrator/TecFuelMix.DbMigrator.csproj
```

## Evidence Files

The script writes these only when commands actually run:

- `docs/evidence/aws-terraform-plan.txt`
- `docs/evidence/aws-terraform-apply.txt`
- `docs/evidence/aws-migration.txt`
- `docs/evidence/aws-migration-response.json`
- `docs/evidence/aws-fetch-invoke.txt`
- `docs/evidence/aws-fetch-invoke-response.json`
- `docs/evidence/aws-queue-after-fetch.json`
- `docs/evidence/aws-read-api-smoke.txt`

The `docs/evidence/aws-*` transcripts are ignored by Git because live output can contain account IDs, URLs, or unredacted values. Commit only a manually redacted evidence summary when live AWS proof needs to be shared.

## Migration Failure Recovery

| Failure | Likely layer | Recovery |
| --- | --- | --- |
| `POSTGRES_* is required.` | Lambda environment or Terraform wiring | Check `aws_lambda_function.migrator` environment variables and Terraform outputs. |
| `SecretsManager` access failure | IAM or VPC endpoint | Check migrator IAM policy, private Secrets Manager endpoint, and Lambda security group egress. |
| Connection timeout or DNS failure | RDS Proxy/VPC path | Verify RDS Proxy target health, private subnets, and security group rules from Lambda clients to RDS Proxy. Do not make RDS public. |
| Authentication failure | Secret mismatch or proxy auth | Confirm admin/writer/read Secrets Manager versions match Terraform variables and that RDS Proxy has all three auth secrets. |
| DbUp script failure | Schema or privilege issue | Stop deployment, keep the transcript, inspect the exact SQL error, fix forward with a new migration script, and rerun. Do not edit an already-applied migration in a shared environment. |
| Migration succeeded but writer/read fails | Runtime role or warm environment | Verify runtime secrets match role passwords, then recycle affected Lambdas if warm environments cached old credentials. |

DbUp records applied scripts. Recover failed deployments by fixing the cause and rerunning the migrator, not by dropping production tables.
