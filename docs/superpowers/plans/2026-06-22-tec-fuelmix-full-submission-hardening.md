# TEC FuelMix Full Submission Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the TEC FuelMix challenge as a complete interview submission with stronger docs, repeatable DB bootstrap, real API auth, hardened Terraform, better observability, and portable tests.

**Architecture:** Keep the existing EventBridge -> Fetch Lambda -> SQS -> Writer Lambda -> RDS Proxy/PostgreSQL ingestion path and API Gateway -> Authorizer -> cache -> Read Lambda -> RDS Proxy/PostgreSQL read path. Keep Terraform as the only published IaC surface and keep Npgsql/raw SQL for PostgreSQL-specific upserts. Add DbUp for migrations, Testcontainers/Respawn for integration tests, and AWS Lambda Powertools for structured runtime telemetry.

**Tech Stack:** .NET 10, AWS Lambda container images, Npgsql, DbUp, xUnit, Testcontainers PostgreSQL, Respawn, AWS Lambda Powertools, Terraform, Docker Compose, PostgreSQL 16.

---

## File Structure

- `README.md`: evaluator entrypoint with architecture diagram, decisions, runbook, traceability, and evidence table.
- `planning/`: human planning inputs used to steer agent/code generation.
- `.dockerignore`: Docker build context guard.
- `src/TecFuelMix.Core/`: parser, DTOs, repository, read queries, connection/secret helpers, migrations embedded or copied for DbUp.
- `src/TecFuelMix.DbMigrator/`: one console app for local/AWS schema and role bootstrap.
- `src/TecFuelMix.FetchLambda/`: scheduled MISO fetch and SQS publish only.
- `src/TecFuelMix.WriterLambda/`: SQS message processing and idempotent DB writes only.
- `src/TecFuelMix.ReadApiLambda/`: API Gateway read API and Lambda authorizer.
- `tests/TecFuelMix.Tests/`: unit and integration tests using Testcontainers and Respawn.
- `infra/terraform/`: the only IaC definition.
- `docs/evidence/`: refreshed verification outputs.

## Task 1: Rename Planning Docs And Make README Canonical

**Files:**
- Move: `project instructions/` -> `planning/`
- Modify: `README.md`
- Modify: `planning/tec-fuelmix-plan.md`
- Modify: `planning/2026-06-22-tec-fuelmix-serverless-elt-implementation-plan.md`

- [ ] **Step 1: Rename the planning directory**

Run:

```powershell
Move-Item -LiteralPath 'project instructions' -Destination 'planning'
```

Expected: `planning/TEC_SeniorEng_TechnicalTest.md`, `planning/tec-fuelmix-plan.md`, and `planning/2026-06-22-tec-fuelmix-serverless-elt-implementation-plan.md` exist.

- [ ] **Step 2: Add planning-note headers**

At the top of `planning/tec-fuelmix-plan.md`, below the title, add:

```markdown
> Planning input: this was the human architecture and decision plan used to steer agent-assisted implementation. The README is the current evaluator-facing truth for what is implemented and verified.
```

At the top of `planning/2026-06-22-tec-fuelmix-serverless-elt-implementation-plan.md`, below the title, add:

```markdown
> Planning input: this task plan was used to generate most of the implementation. Some tasks are superseded by the current README and evidence files.
```

Expected: neither planning doc presents planned live AWS evidence as already captured.

- [ ] **Step 3: Update README planning section**

Add a `Planning Inputs` section to `README.md` after the opening description:

```markdown
## Planning Inputs

The implementation was generated from two human-authored planning artifacts:

- `planning/tec-fuelmix-plan.md`: architecture reasoning, scale decisions, data model, and interview talking points.
- `planning/2026-06-22-tec-fuelmix-serverless-elt-implementation-plan.md`: implementation task plan used for agent-assisted code generation.

These files explain why the system uses isolated ingestion, SQS buffering, API Gateway caching, RDS Proxy, PostgreSQL constraints, Terraform, and raw Npgsql instead of EF Core. The README and `docs/evidence/` are the current implementation and verification surfaces.
```

Expected: evaluator can trace the plan without mistaking it for current evidence.

- [ ] **Step 4: Update README decision table**

Add or refresh a `Decision Record` table in `README.md`:

```markdown
## Decision Record

| Decision | Choice | Why |
| --- | --- | --- |
| Ingestion compute | Scheduled Lambda | Short scheduled job, explicit one-minute cadence, no idle service. |
| Write buffering | SQS + DLQ | Preserves fetched payloads when PostgreSQL is unavailable. |
| Read compute | API Gateway + Lambda | Small operational surface; cache/throttle/concurrency controls protect the DB. |
| DB protection | API cache + Lambda reserved concurrency + RDS Proxy | Stops repeated reads early and caps connection pressure on PostgreSQL. |
| Data access | Npgsql/raw SQL | Upsert/idempotency is PostgreSQL-specific and small; EF Core would add ceremony here. |
| Migrations | DbUp | SQL-first migrations fit the existing schema and make bootstrap rerunnable. |
| Auth | Lambda authorizer + API key usage plan | Bearer token is auth; API key is throttle/quota only. |
| IaC | Terraform only | Avoids duplicate Terraform/CDK stacks while still publishing infrastructure as code. |
```

Expected: decisions from the planning docs are visible in the README.

- [ ] **Step 5: Run docs path check**

Run:

```powershell
rg "project instructions|Planning input|Decision Record" README.md planning
```

Expected: no `README.md` references point to the old `project instructions` path.

- [ ] **Step 6: Commit**

Run:

```powershell
git add README.md planning
git add -u
git commit -m "docs: connect planning inputs to submission readme"
```

Expected: commit succeeds.

## Task 2: Add Docker Build Context Guard

**Files:**
- Create: `.dockerignore`

- [ ] **Step 1: Create `.dockerignore`**

Create `.dockerignore` with:

```gitignore
.git
.github
.vs
.vscode

**/bin/
**/obj/
TestResults/
artifacts/
publish/

.env
.env.*
!.env.example
!.env.*.example
appsettings.Development.json

infra/terraform/.terraform/
**/*.tfstate
**/*.tfstate.*
**/*.tfplan
terraform.tfvars
```

Expected: Docker builds no longer receive Git metadata, build output, local env files, or Terraform state.

- [ ] **Step 2: Build one image**

Run:

```powershell
docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t tec-fuelmix-fetch .
```

Expected: image builds successfully.

- [ ] **Step 3: Commit**

Run:

```powershell
git add .dockerignore
git commit -m "chore: reduce docker build context"
```

Expected: commit succeeds.

## Task 3: Add DbUp Migrator For Schema And Role Bootstrap

**Files:**
- Create: `src/TecFuelMix.DbMigrator/TecFuelMix.DbMigrator.csproj`
- Create: `src/TecFuelMix.DbMigrator/Program.cs`
- Create: `src/TecFuelMix.Core/Migrations/001_schema.sql`
- Create: `src/TecFuelMix.Core/Migrations/002_roles.sql`
- Modify: `TecFuelMix.sln`

- [ ] **Step 1: Create migrator project**

Run:

```powershell
dotnet new console -n TecFuelMix.DbMigrator -o .\src\TecFuelMix.DbMigrator
dotnet sln .\TecFuelMix.sln add .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj
dotnet add .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj package DbUp
```

Expected: project is created, added to the solution, and restores successfully.

- [ ] **Step 2: Embed migration SQL**

Modify `src/TecFuelMix.DbMigrator/TecFuelMix.DbMigrator.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\TecFuelMix.Core\Migrations\*.sql" Link="Migrations\%(Filename)%(Extension)" />
</ItemGroup>
```

Expected: migration SQL is embedded in the migrator assembly.

- [ ] **Step 3: Move schema into first migration**

Create `src/TecFuelMix.Core/Migrations/001_schema.sql` with the current contents of `src/TecFuelMix.Core/Schema.sql`.

Expected: table/index definitions are unchanged.

- [ ] **Step 4: Add rerunnable role bootstrap migration**

Create `src/TecFuelMix.Core/Migrations/002_roles.sql`:

```sql
do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'fuelmix_writer') then
        create role fuelmix_writer login;
    end if;

    if not exists (select 1 from pg_roles where rolname = 'fuelmix_reader') then
        create role fuelmix_reader login;
    end if;
end
$$;

grant connect on database fuelmix to fuelmix_writer, fuelmix_reader;
grant usage on schema public to fuelmix_writer, fuelmix_reader;

grant select, insert, update, delete
    on fuel_mix_snapshots, fuel_mix_readings, ingestion_runs
    to fuelmix_writer;
grant usage, select on all sequences in schema public to fuelmix_writer;

grant select
    on fuel_mix_snapshots, fuel_mix_readings
    to fuelmix_reader;

alter default privileges in schema public
    grant select, insert, update, delete on tables to fuelmix_writer;
alter default privileges in schema public
    grant usage, select on sequences to fuelmix_writer;
alter default privileges in schema public
    grant select on tables to fuelmix_reader;
```

Expected: grants are rerunnable and do not include passwords.

- [ ] **Step 5: Implement migrator**

Replace `src/TecFuelMix.DbMigrator/Program.cs` with:

```csharp
using DbUp;

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_ADMIN_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POSTGRES_ADMIN_CONNECTION_STRING is required.");
    return 2;
}

var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("Database migrations applied.");
return 0;
```

Expected: migrator exits `2` when env var is missing, `0` when migrations succeed.

- [ ] **Step 6: Run migrator locally twice**

Run:

```powershell
$env:POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password'
dotnet run --project .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj
dotnet run --project .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj
```

Expected: first run applies migrations, second run reports no pending migrations.

- [ ] **Step 7: Commit**

Run:

```powershell
git add TecFuelMix.sln src\TecFuelMix.DbMigrator src\TecFuelMix.Core\Migrations
git commit -m "feat: add repeatable database migrator"
```

Expected: commit succeeds.

## Task 4: Add Testcontainers And Respawn Integration Harness

**Files:**
- Modify: `tests/TecFuelMix.Tests/TecFuelMix.Tests.csproj`
- Create: `tests/TecFuelMix.Tests/PostgresFixture.cs`
- Modify: `tests/TecFuelMix.Tests/FuelMixRepositoryTests.cs`

- [ ] **Step 1: Add test packages**

Run:

```powershell
dotnet add .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj package Testcontainers.PostgreSql
dotnet add .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj package Respawn
```

Expected: packages restore successfully.

- [ ] **Step 2: Add PostgreSQL fixture**

Create `tests/TecFuelMix.Tests/PostgresFixture.cs`:

```csharp
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace TecFuelMix.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithDatabase("fuelmix")
        .WithUsername("fuelmix_app")
        .WithPassword("fuelmix_dev_password")
        .Build();

    private Respawner? _respawner;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var schema = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "src", "TecFuelMix.Core", "Migrations", "001_schema.sql"));

        await using (var command = new NpgsqlCommand(schema, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["schemaversions"]
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

Expected: fixture owns its own database and reset path.

- [ ] **Step 3: Convert repository tests to fixture**

Modify `FuelMixRepositoryTests` to use:

```csharp
public sealed class FuelMixRepositoryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public FuelMixRepositoryTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task UpsertSnapshotAsync_is_idempotent_by_source_ref_and_category()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);

        await repository.UpsertSnapshotAsync(snapshot, CancellationToken.None);
        await repository.UpsertSnapshotAsync(snapshot, CancellationToken.None);

        await using var command = dataSource.CreateCommand("""
            select
                (select count(*) from fuel_mix_snapshots) as snapshot_count,
                (select count(*) from fuel_mix_readings) as reading_count
            """);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }
}
```

Expected: repository tests no longer depend on host port `55432`.

- [ ] **Step 4: Run repository tests**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixRepositoryTests
```

Expected: tests pass with a Testcontainers-managed PostgreSQL instance.

- [ ] **Step 5: Commit**

Run:

```powershell
git add tests\TecFuelMix.Tests
git commit -m "test: use isolated postgres integration fixture"
```

Expected: commit succeeds.

## Task 5: Expand Repository Read Queries And Read API

**Files:**
- Modify: `src/TecFuelMix.Core/FuelMixDtos.cs`
- Modify: `src/TecFuelMix.Core/FuelMixRepository.cs`
- Modify: `src/TecFuelMix.ReadApiLambda/Function.cs`
- Modify: `tests/TecFuelMix.Tests/ReadApiValidationTests.cs`
- Modify: `tests/TecFuelMix.Tests/FuelMixRepositoryTests.cs`

- [ ] **Step 1: Add read DTOs**

Add to `src/TecFuelMix.Core/FuelMixDtos.cs`:

```csharp
public sealed record FuelMixSnapshotResponse(
    string SourceRefId,
    DateTime IntervalEst,
    decimal TotalMw,
    IReadOnlyList<FuelMixReading> Readings);

public sealed record FuelMixHistoryRow(
    string SourceRefId,
    DateTime IntervalEst,
    string Category,
    decimal Mw,
    string SourceLabel);

public sealed record IngestionRunStatus(
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Status,
    string? SourceRefId,
    string? ErrorMessage);
```

Expected: response DTOs do not expose raw payloads.

- [ ] **Step 2: Add repository read methods**

Add methods to `FuelMixRepository`:

```csharp
public Task<FuelMixSnapshotResponse?> GetLatestSnapshotAsync(CancellationToken cancellationToken);

public Task<IReadOnlyList<FuelMixHistoryRow>> QueryHistoryAsync(
    DateTime from,
    DateTime to,
    string? category,
    int limit,
    CancellationToken cancellationToken);

public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

public Task<IngestionRunStatus?> GetLatestIngestionRunAsync(CancellationToken cancellationToken);
```

Expected: implementations use parameterized Npgsql commands and `order by interval_est desc`.

- [ ] **Step 3: Add repository read tests**

Add tests named:

```csharp
GetLatestSnapshotAsync_returns_newest_snapshot()
QueryHistoryAsync_filters_by_range_category_and_limit()
GetCategoriesAsync_returns_distinct_categories()
GetLatestIngestionRunAsync_returns_most_recent_run()
```

Expected: each test seeds through `UpsertSnapshotAsync` and reads through repository methods.

- [ ] **Step 4: Expand API routes**

Update `TecFuelMix.ReadApiLambda.Function` routing:

```csharp
return (request.HttpMethod, NormalizePath(request.Path)) switch
{
    ("GET", "/fuel-mix/latest") => await Latest(cancellationToken),
    ("GET", "/fuel-mix") => await History(request.QueryStringParameters, cancellationToken),
    ("GET", "/fuel-mix/categories") => await Categories(cancellationToken),
    ("GET", "/ingestion-runs/latest") => await LatestIngestionRun(cancellationToken),
    ("GET", "/health") => Json(HttpStatusCode.OK, new { status = "ok" }),
    _ => Json(HttpStatusCode.NotFound, new { error = "Route not found." })
};
```

Expected: `/fuel-mix/latest` returns latest data even if harmless query parameters are present.

- [ ] **Step 5: Enforce history bounds**

In `History`, enforce:

```csharp
const int MaxLimit = 500;
const int MaxRangeDays = 7;
```

Rules:

- missing `from` or `to` returns `400`;
- invalid date returns `400`;
- `to <= from` returns `400`;
- range over 7 days returns `400`;
- `limit` defaults to `100`;
- `limit > 500` returns `400`.

Expected: invalid requests fail before database access.

- [ ] **Step 6: Add Read API tests**

Add tests named:

```csharp
Latest_returns_ok_when_snapshot_exists()
History_rejects_missing_dates()
History_rejects_range_over_seven_days()
History_rejects_limit_over_500()
Categories_returns_ok()
LatestIngestionRun_returns_ok()
Health_returns_ok_without_database_query()
```

Expected: route validation is covered without live API Gateway.

- [ ] **Step 7: Run tests**

Run:

```powershell
dotnet test .\TecFuelMix.sln
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src\TecFuelMix.Core src\TecFuelMix.ReadApiLambda tests\TecFuelMix.Tests
git commit -m "feat: expand bounded read api"
```

Expected: commit succeeds.

## Task 6: Add Lambda Authorizer And Runtime Secret Loading

**Files:**
- Modify: `src/TecFuelMix.ReadApiLambda/Function.cs`
- Modify: `src/TecFuelMix.ReadApiLambda/TecFuelMix.ReadApiLambda.csproj`
- Modify: `src/TecFuelMix.Core/TecFuelMix.Core.csproj`
- Create: `src/TecFuelMix.Core/DatabaseSecret.cs`
- Create: `src/TecFuelMix.Core/DatabaseConnectionFactory.cs`
- Modify: Lambda project files that read PostgreSQL connection strings

- [ ] **Step 1: Add AWS Secrets Manager package**

Run:

```powershell
dotnet add .\src\TecFuelMix.Core\TecFuelMix.Core.csproj package AWSSDK.SecretsManager
```

Expected: package restores successfully.

- [ ] **Step 2: Add database secret DTO**

Create `src/TecFuelMix.Core/DatabaseSecret.cs`:

```csharp
namespace TecFuelMix.Core;

public sealed record DatabaseSecret(string Username, string Password);
```

Expected: DTO matches the Terraform secret JSON fields.

- [ ] **Step 3: Add connection factory**

Create `src/TecFuelMix.Core/DatabaseConnectionFactory.cs`:

```csharp
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

namespace TecFuelMix.Core;

public static class DatabaseConnectionFactory
{
    public static async Task<NpgsqlDataSource> CreateAsync(CancellationToken cancellationToken)
    {
        var direct = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return NpgsqlDataSource.Create(direct);
        }

        var host = Required("POSTGRES_HOST");
        var database = Required("POSTGRES_DATABASE");
        var secretArn = Required("POSTGRES_SECRET_ARN");

        using var secrets = new AmazonSecretsManagerClient();
        var response = await secrets.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn
        }, cancellationToken);

        var secret = JsonSerializer.Deserialize<DatabaseSecret>(
            response.SecretString,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (secret is null || string.IsNullOrWhiteSpace(secret.Username) || string.IsNullOrWhiteSpace(secret.Password))
        {
            throw new InvalidOperationException("Database secret must contain username and password.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = 5432,
            Database = database,
            Username = secret.Username,
            Password = secret.Password,
            SslMode = SslMode.Require
        };

        return NpgsqlDataSource.Create(builder.ConnectionString);
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }
}
```

Expected: local tests can still use `POSTGRES_CONNECTION_STRING`; AWS uses Secrets Manager.

- [ ] **Step 4: Add authorizer handler**

In `src/TecFuelMix.ReadApiLambda/Function.cs`, add a handler compatible with API Gateway token authorizers:

```csharp
public APIGatewayCustomAuthorizerResponse Authorize(APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
{
    var expected = Environment.GetEnvironmentVariable("READ_API_BEARER_TOKEN");
    var actual = request.AuthorizationToken?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    var effect = !string.IsNullOrWhiteSpace(expected) && actual == expected ? "Allow" : "Deny";

    return new APIGatewayCustomAuthorizerResponse
    {
        PrincipalID = "external-reader",
        PolicyDocument = new APIGatewayCustomAuthorizerPolicy
        {
            Version = "2012-10-17",
            Statement =
            [
                new APIGatewayCustomAuthorizerPolicy.IAMPolicyStatement
                {
                    Action = ["execute-api:Invoke"],
                    Effect = effect,
                    Resource = [request.MethodArn]
                }
            ]
        }
    };
}
```

Expected: bearer token is the auth boundary; API key remains quota only.

- [ ] **Step 5: Add authorizer tests**

Add tests named:

```csharp
Authorize_allows_matching_bearer_token()
Authorize_denies_missing_token()
Authorize_denies_wrong_token()
```

Expected: policy effect is asserted directly.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet test .\TecFuelMix.sln
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add read api authorizer and secret loading"
```

Expected: commit succeeds.

## Task 7: Add Powertools Telemetry

**Files:**
- Modify: `src/TecFuelMix.FetchLambda/TecFuelMix.FetchLambda.csproj`
- Modify: `src/TecFuelMix.WriterLambda/TecFuelMix.WriterLambda.csproj`
- Modify: `src/TecFuelMix.ReadApiLambda/TecFuelMix.ReadApiLambda.csproj`
- Modify: `src/TecFuelMix.FetchLambda/Function.cs`
- Modify: `src/TecFuelMix.WriterLambda/Function.cs`
- Modify: `src/TecFuelMix.ReadApiLambda/Function.cs`

- [ ] **Step 1: Add Powertools packages**

Run:

```powershell
dotnet add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj package AWS.Lambda.Powertools.Logging
dotnet add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj package AWS.Lambda.Powertools.Metrics
dotnet add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj package AWS.Lambda.Powertools.Logging
dotnet add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj package AWS.Lambda.Powertools.Metrics
dotnet add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj package AWS.Lambda.Powertools.Logging
dotnet add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj package AWS.Lambda.Powertools.Metrics
```

Expected: packages restore successfully.

- [ ] **Step 2: Add minimal metrics**

Use metrics names:

```text
FuelMixFetchSucceeded
FuelMixFetchFailed
FuelMixWriteSucceeded
FuelMixWriteFailed
FuelMixPartialBatchFailures
FuelMixReadRequest
FuelMixLatestSnapshotAgeSeconds
```

Expected: no raw payload, bearer token, or connection string is logged.

- [ ] **Step 3: Add telemetry tests**

Add tests that assert handler responses still succeed/fail as before. Do not test Powertools internals.

Run:

```powershell
dotnet test .\TecFuelMix.sln
```

Expected: all existing behavior still passes.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src tests
git commit -m "feat: add lambda telemetry metrics"
```

Expected: commit succeeds.

## Task 8: Harden Terraform

**Files:**
- Modify: `infra/terraform/lambda.tf`
- Modify: `infra/terraform/api_gateway.tf`
- Modify: `infra/terraform/alarms.tf`
- Modify: `infra/terraform/rds.tf`
- Modify: `infra/terraform/variables.tf`
- Modify: `infra/terraform/outputs.tf`
- Create: `infra/terraform/terraform.tfvars.example`

- [ ] **Step 1: Replace connection-string env vars**

In Lambda environment blocks, replace `POSTGRES_CONNECTION_STRING` with:

```hcl
POSTGRES_HOST       = aws_db_proxy.postgres.endpoint
POSTGRES_DATABASE   = local.db_name
POSTGRES_SECRET_ARN = aws_secretsmanager_secret.writer_db.arn
```

For the read Lambda use `aws_secretsmanager_secret.read_db.arn`.

Expected: Terraform no longer builds full password-bearing connection strings.

- [ ] **Step 2: Grant Lambdas secret access**

Add IAM policy statements:

```hcl
{
  Effect   = "Allow"
  Action   = ["secretsmanager:GetSecretValue"]
  Resource = aws_secretsmanager_secret.writer_db.arn
}
```

and:

```hcl
{
  Effect   = "Allow"
  Action   = ["secretsmanager:GetSecretValue"]
  Resource = aws_secretsmanager_secret.read_db.arn
}
```

Expected: writer can read only writer DB secret; read API can read only reader DB secret.

- [ ] **Step 3: Add authorizer Terraform**

Add:

```hcl
variable "read_api_bearer_token" {
  description = "Bearer token accepted by the read API Lambda authorizer."
  type        = string
  sensitive   = true
}
```

Add `aws_api_gateway_authorizer`, authorizer Lambda wiring, and `READ_API_BEARER_TOKEN` env var on the authorizer Lambda resource or shared read Lambda function.

Expected: `aws_api_gateway_method` data routes use `authorization = "CUSTOM"` and `authorizer_id`.

- [ ] **Step 4: Add explicit dependencies**

Add `depends_on` where AWS validates permissions immediately:

```hcl
depends_on = [
  aws_iam_role_policy_attachment.writer_lambda_vpc,
  aws_iam_role_policy.writer_sqs,
  aws_iam_role_policy.writer_db_secret
]
```

Use equivalent dependency lists for fetch, read API, scheduler, RDS Proxy, and event source mapping.

Expected: first apply order is less brittle.

- [ ] **Step 5: Add log retention**

Create log groups:

```hcl
resource "aws_cloudwatch_log_group" "fetch" {
  name              = "/aws/lambda/${aws_lambda_function.fetch.function_name}"
  retention_in_days = 14
}
```

Repeat for writer and read API.

Expected: logs have bounded retention.

- [ ] **Step 6: Add queue age/backlog and API/RDS alarms**

Add CloudWatch alarms for:

```text
AWS/SQS ApproximateAgeOfOldestMessage on raw queue > 180
AWS/SQS ApproximateNumberOfMessagesVisible on raw queue > 10
AWS/ApiGateway 5XXError on read API > 0
AWS/ApiGateway Latency p95 > 1000ms
AWS/RDS CPUUtilization > 80
AWS/RDS FreeStorageSpace < 2GB
AWS/RDS DatabaseConnections > safe threshold for db.t4g.micro
```

Expected: monitoring covers backlog, API health, and database pressure.

- [ ] **Step 7: Add example tfvars**

Create `infra/terraform/terraform.tfvars.example` with fake values:

```hcl
aws_region = "us-east-1"
project_name = "tec-fuelmix"
vpc_id = "vpc-00000000000000000"
private_subnet_ids = ["subnet-00000000000000000", "subnet-11111111111111111"]
db_admin_password = "replace-me-admin-password"
writer_db_password = "replace-me-writer-password"
read_db_password = "replace-me-reader-password"
read_api_bearer_token = "replace-me-reader-token"
fetch_lambda_image_uri = "000000000000.dkr.ecr.us-east-1.amazonaws.com/tec-fuelmix-fetch:latest"
writer_lambda_image_uri = "000000000000.dkr.ecr.us-east-1.amazonaws.com/tec-fuelmix-writer:latest"
read_api_lambda_image_uri = "000000000000.dkr.ecr.us-east-1.amazonaws.com/tec-fuelmix-read-api:latest"
```

Expected: users see required variables without committing secrets.

- [ ] **Step 8: Validate Terraform**

Run:

```powershell
terraform -chdir=infra/terraform fmt -check
terraform -chdir=infra/terraform validate
```

Expected: both pass.

- [ ] **Step 9: Commit**

Run:

```powershell
git add infra\terraform
git commit -m "feat: harden published terraform infrastructure"
```

Expected: commit succeeds.

## Task 9: Refresh Evidence And README Runbook

**Files:**
- Modify: `README.md`
- Modify: `docs/evidence/*.txt`

- [ ] **Step 1: Capture test evidence**

Run:

```powershell
dotnet test .\TecFuelMix.sln *> .\docs\evidence\01-dotnet-test.txt
```

Expected: evidence file shows all tests passed.

- [ ] **Step 2: Capture local Postgres status**

Run:

```powershell
docker compose ps *> .\docs\evidence\02-local-postgres-status.txt
```

Expected: local compose status is captured, even if integration tests now use Testcontainers.

- [ ] **Step 3: Capture Terraform evidence**

Run:

```powershell
terraform -chdir=infra/terraform fmt -check *> .\docs\evidence\03-terraform-fmt-check.txt
terraform -chdir=infra/terraform validate *> .\docs\evidence\04-terraform-validate.txt
```

Expected: evidence reflects current HEAD.

- [ ] **Step 4: Capture Docker build evidence**

Run:

```powershell
docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t tec-fuelmix-fetch . *> .\docs\evidence\05-docker-fetch-build.txt
docker build -f .\src\TecFuelMix.WriterLambda\Dockerfile -t tec-fuelmix-writer . *> .\docs\evidence\06-docker-writer-build.txt
docker build -f .\src\TecFuelMix.ReadApiLambda\Dockerfile -t tec-fuelmix-read-api . *> .\docs\evidence\07-docker-read-api-build.txt
```

Expected: all three image builds succeed.

- [ ] **Step 5: Capture migrator evidence**

Run:

```powershell
$env:POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password'
dotnet run --project .\src\TecFuelMix.DbMigrator\TecFuelMix.DbMigrator.csproj *> .\docs\evidence\08-dbup-migrator.txt
```

Expected: migration command succeeds or reports no pending migrations.

- [ ] **Step 6: Update README evidence table**

Update the evidence table to list:

```markdown
| File | Command | Result |
| --- | --- | --- |
| `01-dotnet-test.txt` | `dotnet test .\TecFuelMix.sln` | All tests passed |
| `03-terraform-fmt-check.txt` | `terraform -chdir=infra/terraform fmt -check` | Passed |
| `04-terraform-validate.txt` | `terraform -chdir=infra/terraform validate` | Valid |
| `05-docker-fetch-build.txt` | Fetch Lambda Docker build | Built |
| `06-docker-writer-build.txt` | Writer Lambda Docker build | Built |
| `07-docker-read-api-build.txt` | Read API Lambda Docker build | Built |
| `08-dbup-migrator.txt` | DbUp migrator local run | Succeeded or no pending migrations |
```

Expected: README evidence matches files on disk.

- [ ] **Step 7: Commit**

Run:

```powershell
git add README.md docs\evidence
git commit -m "docs: refresh final verification evidence"
```

Expected: commit succeeds.

## Task 10: Final Self-Review

**Files:**
- Inspect: all changed files

- [ ] **Step 1: Run full verification**

Run:

```powershell
dotnet test .\TecFuelMix.sln
terraform -chdir=infra/terraform fmt -check
terraform -chdir=infra/terraform validate
git diff --check HEAD~8..HEAD
```

Expected: all commands pass.

- [ ] **Step 2: Review spec traceability**

Check `planning/TEC_SeniorEng_TechnicalTest.md` against `README.md`.

Expected coverage:

- periodic MISO import: EventBridge Scheduler + Fetch Lambda;
- no more than once per minute: scheduler rate, reserved concurrency, no async retries;
- PostgreSQL: RDS PostgreSQL plus local/Testcontainers PostgreSQL;
- idempotent ingestion: unique constraints and upsert tests;
- safe external view: Lambda authorizer, API key usage plan, read-only DB user, private DB;
- monitoring: Terraform alarms and Powertools metrics;
- containerized: Lambda Dockerfiles and Docker build evidence;
- repeatable IaC: Terraform validate evidence and example variables.

- [ ] **Step 3: Review README truthfulness**

Expected README statements:

- AWS is not deployed in this submission.
- Terraform is published and locally validated.
- DB bootstrap is automated through DbUp, not manual SQL copy/paste.
- API keys are not auth; Lambda authorizer is auth.
- EF Core was intentionally not used.

- [ ] **Step 4: Commit any review-only doc corrections**

Run only if Step 2 or Step 3 changed files:

```powershell
git add README.md planning docs
git commit -m "docs: align submission with final review"
```

Expected: commit succeeds if changes exist.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-22-tec-fuelmix-full-submission-hardening.md`. Two execution options:

**1. Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
