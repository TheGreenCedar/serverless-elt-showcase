# TEC Fuel Mix Serverless ELT

C#/.NET implementation of a serverless ELT flow for MISO real-time fuel mix data.

## Architecture

```mermaid
flowchart LR
    Scheduler[EventBridge Scheduler<br/>rate 1 minute] --> Fetch[Fetch Lambda<br/>reserved concurrency 1]
    Fetch -->|GET MISO FuelMix| MISO[MISO API]
    Fetch -->|raw JSON payload| Queue[SQS raw snapshot queue]
    Queue --> Writer[Writer Lambda<br/>SQS batch + partial failure]
    Writer -->|idempotent upsert| Proxy[RDS Proxy]
    Proxy --> Db[(PostgreSQL)]

    User[External reader] --> Api[API Gateway REST API<br/>API key + cache + throttle]
    Api -->|cache miss| Read[Read API Lambda<br/>reserved concurrency cap]
    Read --> Proxy
    Api -->|cache hit| Cached[Cached JSON response]
```

The fetch path and write path are intentionally separate:

- `TecFuelMix.FetchLambda` is scheduled once per minute. It only calls MISO and publishes the raw payload to SQS through `RAW_SNAPSHOT_QUEUE_URL`.
- `TecFuelMix.WriterLambda` consumes SQS messages and idempotently writes PostgreSQL through `POSTGRES_CONNECTION_STRING`.
- `TecFuelMix.ReadApiLambda` reads PostgreSQL through `POSTGRES_CONNECTION_STRING` and returns the latest ingested snapshot at `/fuel-mix/latest`. That endpoint is latest-only and rejects query parameters; historical filtering is not implemented in this slice.

SQS protects ingestion durability. If PostgreSQL or RDS Proxy is unavailable after MISO returns a snapshot, the raw payload remains queued for retry and eventual DLQ handling. PostgreSQL unique keys keep duplicate SQS deliveries from creating duplicate snapshots or readings.

API Gateway cache, API Gateway usage-plan throttles, Lambda reserved concurrency, and RDS Proxy protect PostgreSQL from external read traffic. Cache hits do not invoke Lambda or touch the database; cache misses still pass through throttles, a Lambda concurrency cap, pooled proxy connections, and bounded SQL.

## Local Verification

Prerequisites:

- .NET SDK matching `global.json`
- Docker Desktop
- Terraform `>= 1.7.0`

Start local PostgreSQL when running from a clean machine:

```powershell
docker compose up -d db
```

Run the local proof commands:

```powershell
dotnet test .\TecFuelMix.sln
docker compose ps
terraform -chdir=infra/terraform validate
```

Build the Lambda container images:

```powershell
docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t tec-fuelmix-fetch .
docker build -f .\src\TecFuelMix.WriterLambda\Dockerfile -t tec-fuelmix-writer .
docker build -f .\src\TecFuelMix.ReadApiLambda\Dockerfile -t tec-fuelmix-read-api .
```

## Deployment Bootstrap

Terraform creates the RDS instance, RDS Proxy secrets, and Lambda environment wiring, but it does not connect to PostgreSQL to create application roles, grant privileges, or apply the schema. For an AWS deployment, run this once from an operator host that can reach the private database endpoint, using the admin credentials created for RDS. This has not been applied to AWS in this local technical challenge.

Apply the schema first:

```powershell
psql "host=<rds-endpoint> port=5432 dbname=fuelmix user=fuelmix_admin sslmode=require" -v ON_ERROR_STOP=1 -f .\src\TecFuelMix.Core\Schema.sql
```

Then bootstrap the app roles and grants. Replace the password placeholders with the same values supplied to Terraform for `writer_db_password` and `read_db_password`.

```sql
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelmix_writer') THEN
        CREATE ROLE fuelmix_writer LOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelmix_reader') THEN
        CREATE ROLE fuelmix_reader LOGIN;
    END IF;
END
$$;

ALTER ROLE fuelmix_writer WITH PASSWORD '<writer-db-password>';
ALTER ROLE fuelmix_reader WITH PASSWORD '<read-db-password>';

GRANT CONNECT ON DATABASE fuelmix TO fuelmix_writer, fuelmix_reader;
GRANT USAGE ON SCHEMA public TO fuelmix_writer, fuelmix_reader;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON fuel_mix_snapshots, fuel_mix_readings, ingestion_runs
    TO fuelmix_writer;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO fuelmix_writer;

GRANT SELECT
    ON fuel_mix_snapshots, fuel_mix_readings
    TO fuelmix_reader;

ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fuelmix_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO fuelmix_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT ON TABLES TO fuelmix_reader;
```

Captured evidence from this workspace is stored in `docs/evidence`:

| File | Command | Result |
| --- | --- | --- |
| `01-dotnet-test.txt` | `dotnet test .\TecFuelMix.sln` | Passed: 13 tests, 0 failed |
| `02-local-postgres-status.txt` | `docker compose ps` | `db` container running and healthy on host port `55432` |
| `03-terraform-validate.txt` | `terraform -chdir=infra/terraform validate` | Terraform configuration valid |
| `04-docker-fetch-build.txt` | Fetch Lambda Docker build | Image `tec-fuelmix-fetch:latest` built |
| `05-docker-writer-build.txt` | Writer Lambda Docker build | Image `tec-fuelmix-writer:latest` built |
| `06-docker-read-api-build.txt` | Read API Lambda Docker build | Image `tec-fuelmix-read-api:latest` built |

## Scale And Safety Controls

| Control | Where | Why it exists |
| --- | --- | --- |
| One-minute schedule with zero Scheduler retries | `aws_scheduler_schedule.fetch` | Keeps EventBridge Scheduler from re-submitting a failed scheduled invocation. |
| Fetch async retries disabled with 60-second event age | `aws_lambda_function_event_invoke_config.fetch` | Meets the source polling limit by preventing Lambda async retries from re-running the MISO fetch after a post-GET failure. |
| Fetch reserved concurrency `1` | `aws_lambda_function.fetch` | Prevents overlapping MISO fetches from the scheduled path. |
| Fetch async failure DLQ + alarms | `aws_sqs_queue.fetch_async_failure_dlq` and fetch alarms | Preserves terminal async invoke failure details and surfaces fetch failures without re-polling MISO. |
| SQS raw snapshot queue + DLQ | `aws_sqs_queue.raw_snapshot` | Decouples MISO fetch success from database availability and preserves failed writes for retry/redrive. |
| Writer reserved concurrency `1` | `aws_lambda_function.writer` | Keeps database write pressure predictable. |
| Partial batch failure | SQS event source mapping and writer response | Retries only failed SQS records instead of replaying a whole successful batch. |
| PostgreSQL unique keys | `src/TecFuelMix.Core/Schema.sql` | Makes duplicate delivery safe by enforcing one snapshot per `source_ref_id` and one reading per snapshot/category. |
| API Gateway REST cache | `aws_api_gateway_stage` and method settings | Absorbs repeated reads before Lambda or PostgreSQL are involved. |
| API key + usage plan throttle | `aws_api_gateway_usage_plan` | Caps external client request rate and burst size. |
| Read Lambda reserved concurrency | `read_api_reserved_concurrency` | Backstops cache misses so user traffic cannot fan out into unbounded database connections. |
| RDS Proxy pool limits | `aws_db_proxy_default_target_group.postgres` | Pools Lambda database connections and caps pressure on PostgreSQL. |
| Private RDS security groups | `infra/terraform/rds.tf` | Allows PostgreSQL traffic only through RDS Proxy from Lambda clients. |

## Known Boundaries

- Terraform was validated locally only. No `terraform plan` or `terraform apply` was run against AWS.
- RDS role grants and schema/bootstrap execution are documented above but were not applied to AWS. Terraform declares database users/secrets for RDS Proxy auth; PostgreSQL still needs the operational bootstrap step during deployment.
- Lambda runtime secret retrieval is a follow-up. The current app reads full PostgreSQL connection strings from environment variables.
- Live MISO, live SQS delivery, API Gateway cache hit/miss behavior, and RDS Proxy behavior are not exercised by the local evidence.
- Local tests use Docker Compose PostgreSQL on port `55432`; they do not prove AWS networking, IAM, or managed-service behavior.
