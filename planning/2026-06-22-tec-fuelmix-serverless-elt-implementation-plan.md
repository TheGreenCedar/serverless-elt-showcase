# TEC FuelMix Serverless ELT Implementation Plan

> Planning input: this task plan was used to generate most of the implementation. Some tasks are superseded by the current README and evidence files.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C# serverless ELT system that fetches MISO FuelMix data once per minute, buffers it through SQS, writes idempotently to PostgreSQL, and exposes cached safe read APIs.

**Architecture:** EventBridge invokes a fetch Lambda that calls MISO and publishes raw snapshots to SQS. A writer Lambda consumes SQS messages and upserts PostgreSQL through RDS Proxy. A read Lambda sits behind API Gateway REST API caching, usage plans, throttles, and RDS Proxy so read spikes do not flood PostgreSQL.

**Tech Stack:** .NET 10, AWS Lambda container images, Amazon SQS, API Gateway REST API cache, Amazon RDS PostgreSQL, RDS Proxy, Terraform, Docker Compose, Npgsql, xUnit.

---

## File Structure

Create this structure from the workspace root `C:\Users\alber\OneDrive\Documents\TEC_TechnicalTest`:

```text
src/
  TecFuelMix.Core/
    FuelMixDtos.cs
    FuelMixParser.cs
    FuelMixRepository.cs
    Schema.sql
  TecFuelMix.FetchLambda/
    Function.cs
    Dockerfile
  TecFuelMix.WriterLambda/
    Function.cs
    Dockerfile
  TecFuelMix.ReadApiLambda/
    Function.cs
    Dockerfile
tests/
  TecFuelMix.Tests/
    SamplePayloads.cs
    FuelMixParserTests.cs
    FuelMixRepositoryTests.cs
    ReadApiValidationTests.cs
infra/
  terraform/
    main.tf
    variables.tf
    outputs.tf
    rds.tf
    sqs.tf
    lambda.tf
    api_gateway.tf
    alarms.tf
docs/
  evidence/
```

File responsibilities:

- `TecFuelMix.Core`: pure parsing, DTOs, SQL repository, and schema. No Lambda event types here.
- `TecFuelMix.FetchLambda`: scheduled MISO fetch and SQS publish only.
- `TecFuelMix.WriterLambda`: SQS message processing and idempotent database writes only.
- `TecFuelMix.ReadApiLambda`: API Gateway request parsing, query validation, and read DTO responses only.
- `infra/terraform`: repeatable AWS infrastructure.
- `docs/evidence`: saved command outputs and proof artifacts.

## Task 1: Scaffold The Solution

**Files:**
- Create: `global.json`
- Create: `TecFuelMix.sln`
- Create: `src/TecFuelMix.Core/TecFuelMix.Core.csproj`
- Create: `src/TecFuelMix.FetchLambda/TecFuelMix.FetchLambda.csproj`
- Create: `src/TecFuelMix.WriterLambda/TecFuelMix.WriterLambda.csproj`
- Create: `src/TecFuelMix.ReadApiLambda/TecFuelMix.ReadApiLambda.csproj`
- Create: `tests/TecFuelMix.Tests/TecFuelMix.Tests.csproj`

- [ ] **Step 1: Pin the local SDK**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.301",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 2: Create solution and projects**

Run:

```powershell
dotnet new sln -n TecFuelMix
dotnet new classlib -n TecFuelMix.Core -o .\src\TecFuelMix.Core
dotnet new classlib -n TecFuelMix.FetchLambda -o .\src\TecFuelMix.FetchLambda
dotnet new classlib -n TecFuelMix.WriterLambda -o .\src\TecFuelMix.WriterLambda
dotnet new classlib -n TecFuelMix.ReadApiLambda -o .\src\TecFuelMix.ReadApiLambda
dotnet new xunit -n TecFuelMix.Tests -o .\tests\TecFuelMix.Tests
dotnet sln .\TecFuelMix.sln add .\src\TecFuelMix.Core\TecFuelMix.Core.csproj
dotnet sln .\TecFuelMix.sln add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj
dotnet sln .\TecFuelMix.sln add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj
dotnet sln .\TecFuelMix.sln add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj
dotnet sln .\TecFuelMix.sln add .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj
```

Expected: each command exits `0`.

- [ ] **Step 3: Add project references and packages**

Run:

```powershell
dotnet add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj reference .\src\TecFuelMix.Core\TecFuelMix.Core.csproj
dotnet add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj reference .\src\TecFuelMix.Core\TecFuelMix.Core.csproj
dotnet add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj reference .\src\TecFuelMix.Core\TecFuelMix.Core.csproj
dotnet add .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj reference .\src\TecFuelMix.Core\TecFuelMix.Core.csproj
dotnet add .\src\TecFuelMix.Core\TecFuelMix.Core.csproj package Npgsql
dotnet add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj package Amazon.Lambda.Core
dotnet add .\src\TecFuelMix.FetchLambda\TecFuelMix.FetchLambda.csproj package AWSSDK.SQS
dotnet add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj package Amazon.Lambda.Core
dotnet add .\src\TecFuelMix.WriterLambda\TecFuelMix.WriterLambda.csproj package Amazon.Lambda.SQSEvents
dotnet add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj package Amazon.Lambda.APIGatewayEvents
dotnet add .\src\TecFuelMix.ReadApiLambda\TecFuelMix.ReadApiLambda.csproj package Amazon.Lambda.Core
```

Expected: package restore succeeds.

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build .\TecFuelMix.sln
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

Run:

```powershell
git add global.json TecFuelMix.sln src tests
git commit -m "chore: scaffold fuel mix solution"
```

Expected: commit succeeds.

## Task 2: Implement FuelMix Parsing

**Files:**
- Create: `src/TecFuelMix.Core/FuelMixDtos.cs`
- Create: `src/TecFuelMix.Core/FuelMixParser.cs`
- Create: `tests/TecFuelMix.Tests/SamplePayloads.cs`
- Create: `tests/TecFuelMix.Tests/FuelMixParserTests.cs`

- [ ] **Step 1: Write the failing parser tests**

Create `tests/TecFuelMix.Tests/SamplePayloads.cs`:

```csharp
namespace TecFuelMix.Tests;

internal static class SamplePayloads
{
    public const string FuelMixJson = """
    {
      "RefId": "22-Jun-2026 - Interval 11:05 EST",
      "TotalMW": "82968",
      "Fuel": {
        "Type": [
          {
            "INTERVALEST": "2026-06-22 11:05:00 AM",
            "CATEGORY": "Coal",
            "ACT": "26869",
            "FUEL_CATEGORY": "Coal  (26,869 MW)"
          },
          {
            "INTERVALEST": "2026-06-22 11:05:00 AM",
            "CATEGORY": "Battery Storage",
            "ACT": "-420",
            "FUEL_CATEGORY": "Battery Storage  (-420 MW)"
          }
        ]
      }
    }
    """;
}
```

Create `tests/TecFuelMix.Tests/FuelMixParserTests.cs`:

```csharp
using TecFuelMix.Core;

namespace TecFuelMix.Tests;

public sealed class FuelMixParserTests
{
    [Fact]
    public void Parse_converts_snapshot_and_readings()
    {
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);

        Assert.Equal("22-Jun-2026 - Interval 11:05 EST", snapshot.SourceRefId);
        Assert.Equal(new DateTime(2026, 6, 22, 11, 5, 0), snapshot.IntervalEst);
        Assert.Equal(82968m, snapshot.TotalMw);
        Assert.Equal(2, snapshot.Readings.Count);
        Assert.Contains(snapshot.Readings, r => r.Category == "Coal" && r.Mw == 26869m);
        Assert.Contains(snapshot.Readings, r => r.Category == "Battery Storage" && r.Mw == -420m);
    }

    [Fact]
    public void Parse_rejects_empty_payload()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FuelMixParser.Parse("{}"));

        Assert.Contains("RefId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run parser tests and verify failure**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixParserTests
```

Expected: FAIL because `FuelMixParser` and DTOs do not exist.

- [ ] **Step 3: Add DTOs**

Create `src/TecFuelMix.Core/FuelMixDtos.cs`:

```csharp
namespace TecFuelMix.Core;

public sealed record FuelMixSnapshot(
    string SourceRefId,
    DateTime IntervalEst,
    decimal TotalMw,
    string RawPayload,
    IReadOnlyList<FuelMixReading> Readings);

public sealed record FuelMixReading(
    string Category,
    decimal Mw,
    string SourceLabel);
```

- [ ] **Step 4: Add parser**

Create `src/TecFuelMix.Core/FuelMixParser.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace TecFuelMix.Core;

public static class FuelMixParser
{
    public static FuelMixSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var refId = RequiredString(root, "RefId");
        var totalMw = ParseDecimal(RequiredString(root, "TotalMW"), "TotalMW");
        var rows = root.GetProperty("Fuel").GetProperty("Type").EnumerateArray().ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Fuel.Type contains no readings.");
        }

        var interval = ParseInterval(RequiredString(rows[0], "INTERVALEST"));
        var readings = rows.Select(row => new FuelMixReading(
            RequiredString(row, "CATEGORY"),
            ParseDecimal(RequiredString(row, "ACT"), "ACT"),
            RequiredString(row, "FUEL_CATEGORY"))).ToArray();

        return new FuelMixSnapshot(refId, interval, totalMw, json, readings);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Missing required string field '{name}'.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Missing required string field '{name}'.");
        }

        return text;
    }

    private static decimal ParseDecimal(string text, string fieldName)
    {
        var normalized = text.Replace(",", "", StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Field '{fieldName}' is not a decimal value.");
        }

        return value;
    }

    private static DateTime ParseInterval(string text)
    {
        if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd h:mm:ss tt",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            throw new InvalidOperationException("INTERVALEST is not in the expected source format.");
        }

        return value;
    }
}
```

- [ ] **Step 5: Run parser tests**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixParserTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\TecFuelMix.Core tests\TecFuelMix.Tests
git commit -m "feat: parse MISO fuel mix payload"
```

Expected: commit succeeds.

## Task 3: Add PostgreSQL Schema And Idempotent Repository

**Files:**
- Create: `docker-compose.yml`
- Create: `src/TecFuelMix.Core/Schema.sql`
- Create: `src/TecFuelMix.Core/FuelMixRepository.cs`
- Create: `tests/TecFuelMix.Tests/FuelMixRepositoryTests.cs`

- [ ] **Step 1: Add local PostgreSQL**

Create `docker-compose.yml`:

```yaml
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_DB: fuelmix
      POSTGRES_USER: fuelmix_app
      POSTGRES_PASSWORD: fuelmix_dev_password
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fuelmix_app -d fuelmix"]
      interval: 5s
      timeout: 3s
      retries: 10
```

- [ ] **Step 2: Add schema**

Create `src/TecFuelMix.Core/Schema.sql`:

```sql
create table if not exists fuel_mix_snapshots (
    id bigserial primary key,
    source_ref_id text not null unique,
    interval_est timestamp without time zone not null,
    total_mw numeric(12,3) not null,
    raw_payload jsonb not null,
    imported_at timestamptz not null default now()
);

create table if not exists fuel_mix_readings (
    snapshot_id bigint not null references fuel_mix_snapshots(id) on delete cascade,
    category text not null,
    mw numeric(12,3) not null,
    source_label text not null,
    primary key (snapshot_id, category)
);

create table if not exists ingestion_runs (
    id bigserial primary key,
    started_at timestamptz not null default now(),
    completed_at timestamptz,
    status text not null,
    source_ref_id text,
    error_message text
);

create index if not exists ix_fuel_mix_snapshots_interval_est
    on fuel_mix_snapshots (interval_est desc);

create index if not exists ix_fuel_mix_readings_category_snapshot
    on fuel_mix_readings (category, snapshot_id);
```

- [ ] **Step 3: Write failing repository test**

Create `tests/TecFuelMix.Tests/FuelMixRepositoryTests.cs`:

```csharp
using Npgsql;
using TecFuelMix.Core;

namespace TecFuelMix.Tests;

public sealed class FuelMixRepositoryTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password";

    [Fact]
    public async Task UpsertSnapshotAsync_is_idempotent_by_source_ref_and_category()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
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

    private static async Task ResetDatabase(NpgsqlDataSource dataSource)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine("..", "..", "..", "..", "src", "TecFuelMix.Core", "Schema.sql"));
        await using (var schemaCommand = dataSource.CreateCommand(schema))
        {
            await schemaCommand.ExecuteNonQueryAsync();
        }

        await using var cleanup = dataSource.CreateCommand("""
            truncate table ingestion_runs, fuel_mix_readings, fuel_mix_snapshots restart identity cascade;
            """);
        await cleanup.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 4: Start PostgreSQL**

Run:

```powershell
docker compose up -d db
```

Expected: PostgreSQL container starts.

- [ ] **Step 5: Run repository test and verify failure**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixRepositoryTests
```

Expected: FAIL because `FuelMixRepository` does not exist.

- [ ] **Step 6: Add repository**

Create `src/TecFuelMix.Core/FuelMixRepository.cs`:

```csharp
using Npgsql;

namespace TecFuelMix.Core;

public sealed class FuelMixRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public FuelMixRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<long> UpsertSnapshotAsync(FuelMixSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var snapshotId = await UpsertSnapshotRow(connection, snapshot, cancellationToken);
        foreach (var reading in snapshot.Readings)
        {
            await UpsertReadingRow(connection, snapshotId, reading, cancellationToken);
        }

        await using var run = new NpgsqlCommand("""
            insert into ingestion_runs (completed_at, status, source_ref_id)
            values (now(), 'succeeded', @source_ref_id);
            """, connection, transaction);
        run.Parameters.AddWithValue("source_ref_id", snapshot.SourceRefId);
        await run.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return snapshotId;
    }

    private static async Task<long> UpsertSnapshotRow(
        NpgsqlConnection connection,
        FuelMixSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into fuel_mix_snapshots (source_ref_id, interval_est, total_mw, raw_payload)
            values (@source_ref_id, @interval_est, @total_mw, @raw_payload::jsonb)
            on conflict (source_ref_id)
            do update set
                interval_est = excluded.interval_est,
                total_mw = excluded.total_mw,
                raw_payload = excluded.raw_payload
            returning id;
            """, connection);
        command.Parameters.AddWithValue("source_ref_id", snapshot.SourceRefId);
        command.Parameters.AddWithValue("interval_est", snapshot.IntervalEst);
        command.Parameters.AddWithValue("total_mw", snapshot.TotalMw);
        command.Parameters.AddWithValue("raw_payload", snapshot.RawPayload);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static async Task UpsertReadingRow(
        NpgsqlConnection connection,
        long snapshotId,
        FuelMixReading reading,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into fuel_mix_readings (snapshot_id, category, mw, source_label)
            values (@snapshot_id, @category, @mw, @source_label)
            on conflict (snapshot_id, category)
            do update set
                mw = excluded.mw,
                source_label = excluded.source_label;
            """, connection);
        command.Parameters.AddWithValue("snapshot_id", snapshotId);
        command.Parameters.AddWithValue("category", reading.Category);
        command.Parameters.AddWithValue("mw", reading.Mw);
        command.Parameters.AddWithValue("source_label", reading.SourceLabel);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

- [ ] **Step 7: Run repository tests**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixRepositoryTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```powershell
git add docker-compose.yml src\TecFuelMix.Core tests\TecFuelMix.Tests
git commit -m "feat: persist fuel mix snapshots idempotently"
```

Expected: commit succeeds.

## Task 4: Implement Fetch Lambda And SQS Publish

**Files:**
- Create: `src/TecFuelMix.FetchLambda/Function.cs`
- Create: `src/TecFuelMix.FetchLambda/Dockerfile`

- [ ] **Step 1: Add fetch Lambda function**

Create `src/TecFuelMix.FetchLambda/Function.cs`:

```csharp
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.FetchLambda;

public sealed class Function
{
    private static readonly Uri FuelMixUri = new("https://public-api.misoenergy.org/api/FuelMix");
    private readonly HttpClient _httpClient;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;

    public Function()
        : this(new HttpClient(), new AmazonSQSClient(), Environment.GetEnvironmentVariable("RAW_SNAPSHOT_QUEUE_URL") ?? "")
    {
    }

    public Function(HttpClient httpClient, IAmazonSQS sqs, string queueUrl)
    {
        _httpClient = httpClient;
        _sqs = sqs;
        _queueUrl = queueUrl;
    }

    public async Task Handler(object input, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(_queueUrl))
        {
            throw new InvalidOperationException("RAW_SNAPSHOT_QUEUE_URL is required.");
        }

        using var response = await _httpClient.GetAsync(FuelMixUri, context.CancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(context.CancellationToken);
        var snapshot = FuelMixParser.Parse(json);

        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = json,
            MessageAttributes =
            {
                ["source_ref_id"] = new MessageAttributeValue { DataType = "String", StringValue = snapshot.SourceRefId },
                ["fetched_at_utc"] = new MessageAttributeValue { DataType = "String", StringValue = DateTimeOffset.UtcNow.ToString("O") }
            }
        }, context.CancellationToken);

        context.Logger.LogInformation($"Queued MISO FuelMix snapshot {snapshot.SourceRefId}.");
    }
}
```

- [ ] **Step 2: Add Dockerfile**

Create `src/TecFuelMix.FetchLambda/Dockerfile`:

```dockerfile
FROM public.ecr.aws/lambda/dotnet:10 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TecFuelMix.FetchLambda/TecFuelMix.FetchLambda.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /var/task
COPY --from=build /app/publish .
CMD ["TecFuelMix.FetchLambda::TecFuelMix.FetchLambda.Function::Handler"]
```

- [ ] **Step 3: Build fetch image**

Run:

```powershell
docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t tec-fuelmix-fetch .
```

Expected: image builds.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\TecFuelMix.FetchLambda
git commit -m "feat: add scheduled MISO fetch lambda"
```

Expected: commit succeeds.

## Task 5: Implement Writer Lambda

**Files:**
- Create: `src/TecFuelMix.WriterLambda/Function.cs`
- Create: `src/TecFuelMix.WriterLambda/Dockerfile`

- [ ] **Step 1: Add writer Lambda function**

Create `src/TecFuelMix.WriterLambda/Function.cs`:

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.WriterLambda;

public sealed class Function
{
    private readonly NpgsqlDataSource _dataSource;

    public Function()
        : this(NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? ""))
    {
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<SQSBatchResponse> Handler(SQSEvent evnt, ILambdaContext context)
    {
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        var repository = new FuelMixRepository(_dataSource);

        foreach (var record in evnt.Records)
        {
            try
            {
                var snapshot = FuelMixParser.Parse(record.Body);
                await repository.UpsertSnapshotAsync(snapshot, context.CancellationToken);
                context.Logger.LogInformation($"Persisted MISO FuelMix snapshot {snapshot.SourceRefId}.");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Failed to persist SQS message {record.MessageId}: {ex}");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        return new SQSBatchResponse(failures);
    }
}
```

- [ ] **Step 2: Add Dockerfile**

Create `src/TecFuelMix.WriterLambda/Dockerfile`:

```dockerfile
FROM public.ecr.aws/lambda/dotnet:10 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TecFuelMix.WriterLambda/TecFuelMix.WriterLambda.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /var/task
COPY --from=build /app/publish .
CMD ["TecFuelMix.WriterLambda::TecFuelMix.WriterLambda.Function::Handler"]
```

- [ ] **Step 3: Build writer image**

Run:

```powershell
docker build -f .\src\TecFuelMix.WriterLambda\Dockerfile -t tec-fuelmix-writer .
```

Expected: image builds.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src\TecFuelMix.WriterLambda
git commit -m "feat: add SQS fuel mix writer lambda"
```

Expected: commit succeeds.

## Task 6: Implement Read API Lambda

**Files:**
- Modify: `src/TecFuelMix.Core/FuelMixRepository.cs`
- Create: `src/TecFuelMix.ReadApiLambda/Function.cs`
- Create: `src/TecFuelMix.ReadApiLambda/Dockerfile`
- Create: `tests/TecFuelMix.Tests/ReadApiValidationTests.cs`

- [ ] **Step 1: Add failing validation tests**

Create `tests/TecFuelMix.Tests/ReadApiValidationTests.cs`:

```csharp
using TecFuelMix.ReadApiLambda;

namespace TecFuelMix.Tests;

public sealed class ReadApiValidationTests
{
    [Fact]
    public void QueryOptions_caps_limit()
    {
        var options = QueryOptions.From(new Dictionary<string, string> { ["limit"] = "5000" });

        Assert.Equal(500, options.Limit);
    }

    [Fact]
    public void QueryOptions_rejects_date_ranges_over_seven_days()
    {
        var query = new Dictionary<string, string>
        {
            ["from"] = "2026-06-01T00:00:00",
            ["to"] = "2026-06-20T00:00:00"
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => QueryOptions.From(query));
    }
}
```

- [ ] **Step 2: Run validation tests and verify failure**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter ReadApiValidationTests
```

Expected: FAIL because `QueryOptions` does not exist.

- [ ] **Step 3: Add read query methods to repository**

Append this code inside `FuelMixRepository`:

```csharp
public async Task<FuelMixSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
{
    await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand("""
        select id, source_ref_id, interval_est, total_mw, raw_payload
        from fuel_mix_snapshots
        order by interval_est desc
        limit 1;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
        return null;
    }

    var id = reader.GetInt64(0);
    var snapshot = new FuelMixSnapshot(
        reader.GetString(1),
        reader.GetDateTime(2),
        reader.GetDecimal(3),
        reader.GetString(4),
        await GetReadingsAsync(id, cancellationToken));
    return snapshot;
}

private async Task<IReadOnlyList<FuelMixReading>> GetReadingsAsync(long snapshotId, CancellationToken cancellationToken)
{
    await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand("""
        select category, mw, source_label
        from fuel_mix_readings
        where snapshot_id = @snapshot_id
        order by category;
        """, connection);
    command.Parameters.AddWithValue("snapshot_id", snapshotId);

    var readings = new List<FuelMixReading>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        readings.Add(new FuelMixReading(reader.GetString(0), reader.GetDecimal(1), reader.GetString(2)));
    }

    return readings;
}
```

- [ ] **Step 4: Add read API function**

Create `src/TecFuelMix.ReadApiLambda/Function.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.ReadApiLambda;

public sealed record QueryOptions(DateTime? From, DateTime? To, string? Category, int Limit)
{
    public static QueryOptions From(IReadOnlyDictionary<string, string>? query)
    {
        query ??= new Dictionary<string, string>();
        var limit = query.TryGetValue("limit", out var rawLimit) && int.TryParse(rawLimit, out var parsed)
            ? Math.Clamp(parsed, 1, 500)
            : 100;
        var from = query.TryGetValue("from", out var rawFrom) ? DateTime.Parse(rawFrom) : (DateTime?)null;
        var to = query.TryGetValue("to", out var rawTo) ? DateTime.Parse(rawTo) : (DateTime?)null;
        if (from.HasValue && to.HasValue && to.Value - from.Value > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Date range cannot exceed 7 days.");
        }

        query.TryGetValue("category", out var category);
        return new QueryOptions(from, to, string.IsNullOrWhiteSpace(category) ? null : category, limit);
    }
}

public sealed class Function
{
    private readonly FuelMixRepository _repository;

    public Function()
        : this(new FuelMixRepository(NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? "")))
    {
    }

    public Function(FuelMixRepository repository)
    {
        _repository = repository;
    }

    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            if (request.Path.EndsWith("/fuel-mix/latest", StringComparison.OrdinalIgnoreCase))
            {
                var latest = await _repository.GetLatestSnapshotAsync(context.CancellationToken);
                if (latest is null)
                {
                    return Json(HttpStatusCode.NotFound, new { error = "No fuel mix snapshots have been ingested." });
                }

                return Json(HttpStatusCode.OK, new
                {
                    refId = latest.SourceRefId,
                    intervalEst = latest.IntervalEst,
                    totalMw = latest.TotalMw,
                    fuels = latest.Readings.Select(r => new { category = r.Category, mw = r.Mw })
                });
            }

            return Json(HttpStatusCode.NotFound, new { error = "Route not found." });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Json(HttpStatusCode.BadRequest, new { error = ex.Message });
        }
    }

    private static APIGatewayProxyResponse Json(HttpStatusCode statusCode, object body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)statusCode,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = JsonSerializer.Serialize(body)
        };
    }
}
```

- [ ] **Step 5: Add Dockerfile**

Create `src/TecFuelMix.ReadApiLambda/Dockerfile`:

```dockerfile
FROM public.ecr.aws/lambda/dotnet:10 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TecFuelMix.ReadApiLambda/TecFuelMix.ReadApiLambda.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /var/task
COPY --from=build /app/publish .
CMD ["TecFuelMix.ReadApiLambda::TecFuelMix.ReadApiLambda.Function::Handler"]
```

- [ ] **Step 6: Run read tests and build**

Run:

```powershell
dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter ReadApiValidationTests
docker build -f .\src\TecFuelMix.ReadApiLambda\Dockerfile -t tec-fuelmix-read-api .
```

Expected: tests pass and image builds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src\TecFuelMix.Core src\TecFuelMix.ReadApiLambda tests\TecFuelMix.Tests
git commit -m "feat: add cached read API lambda surface"
```

Expected: commit succeeds.

## Task 7: Add Terraform Skeleton

**Files:**
- Create: `infra/terraform/main.tf`
- Create: `infra/terraform/variables.tf`
- Create: `infra/terraform/sqs.tf`
- Create: `infra/terraform/rds.tf`
- Create: `infra/terraform/lambda.tf`
- Create: `infra/terraform/api_gateway.tf`
- Create: `infra/terraform/alarms.tf`
- Create: `infra/terraform/outputs.tf`

- [ ] **Step 1: Add provider and variables**

Create `infra/terraform/main.tf`:

```hcl
terraform {
  required_version = ">= 1.7.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}
```

Create `infra/terraform/variables.tf`:

```hcl
variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "project_name" {
  type    = string
  default = "tec-fuelmix"
}

variable "read_api_cache_ttl_seconds" {
  type    = number
  default = 30
}

variable "vpc_id" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "db_password" {
  type      = string
  sensitive = true
}

variable "fetch_lambda_image_uri" {
  type = string
}

variable "writer_lambda_image_uri" {
  type = string
}

variable "read_api_lambda_image_uri" {
  type = string
}
```

- [ ] **Step 2: Add SQS resources**

Create `infra/terraform/sqs.tf`:

```hcl
resource "aws_sqs_queue" "raw_snapshot_dlq" {
  name = "${var.project_name}-raw-snapshot-dlq"
}

resource "aws_sqs_queue" "raw_snapshot" {
  name                       = "${var.project_name}-raw-snapshot"
  visibility_timeout_seconds = 60

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.raw_snapshot_dlq.arn
    maxReceiveCount     = 5
  })
}
```

- [ ] **Step 3: Add RDS, RDS Proxy, and security groups**

Create `infra/terraform/rds.tf`:

```hcl
resource "aws_security_group" "lambda" {
  name        = "${var.project_name}-lambda"
  description = "Lambda egress to RDS Proxy"
  vpc_id      = var.vpc_id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_security_group" "postgres" {
  name        = "${var.project_name}-postgres"
  description = "PostgreSQL from Lambda security group only"
  vpc_id      = var.vpc_id

  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.lambda.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_db_subnet_group" "postgres" {
  name       = "${var.project_name}-db-subnets"
  subnet_ids = var.private_subnet_ids
}

resource "aws_secretsmanager_secret" "db" {
  name = "${var.project_name}-db"
}

resource "aws_secretsmanager_secret_version" "db" {
  secret_id = aws_secretsmanager_secret.db.id
  secret_string = jsonencode({
    username = "fuelmix_app"
    password = var.db_password
  })
}

resource "aws_db_instance" "postgres" {
  identifier             = "${var.project_name}-postgres"
  engine                 = "postgres"
  engine_version         = "16"
  instance_class         = "db.t4g.micro"
  allocated_storage      = 20
  db_name                = "fuelmix"
  username               = "fuelmix_app"
  password               = var.db_password
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.postgres.id]
  publicly_accessible    = false
  skip_final_snapshot    = true
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
  role = aws_iam_role.rds_proxy.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = aws_secretsmanager_secret.db.arn
    }]
  })
}

resource "aws_db_proxy" "postgres" {
  name                   = "${var.project_name}-postgres-proxy"
  engine_family          = "POSTGRESQL"
  role_arn               = aws_iam_role.rds_proxy.arn
  vpc_subnet_ids         = var.private_subnet_ids
  vpc_security_group_ids = [aws_security_group.lambda.id]

  auth {
    auth_scheme = "SECRETS"
    iam_auth    = "DISABLED"
    secret_arn  = aws_secretsmanager_secret.db.arn
  }
}

resource "aws_db_proxy_default_target_group" "postgres" {
  db_proxy_name = aws_db_proxy.postgres.name

  connection_pool_config {
    max_connections_percent      = 70
    max_idle_connections_percent = 50
    connection_borrow_timeout    = 30
  }
}

resource "aws_db_proxy_target" "postgres" {
  db_instance_identifier = aws_db_instance.postgres.identifier
  db_proxy_name          = aws_db_proxy.postgres.name
  target_group_name      = aws_db_proxy_default_target_group.postgres.name
}
```

- [ ] **Step 4: Add Lambda resources and schedule**

Create `infra/terraform/lambda.tf`:

```hcl
data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "lambda" {
  name               = "${var.project_name}-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role_policy_attachment" "lambda_basic" {
  role       = aws_iam_role.lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy_attachment" "lambda_vpc" {
  role       = aws_iam_role.lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

resource "aws_iam_role_policy" "lambda_app" {
  role = aws_iam_role.lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["sqs:SendMessage", "sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
        Resource = [aws_sqs_queue.raw_snapshot.arn, aws_sqs_queue.raw_snapshot_dlq.arn]
      },
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = aws_secretsmanager_secret.db.arn
      }
    ]
  })
}

locals {
  postgres_connection_string = "Host=${aws_db_proxy.postgres.endpoint};Port=5432;Database=fuelmix;Username=fuelmix_app;Password=${var.db_password}"
}

resource "aws_lambda_function" "fetch" {
  function_name                  = "${var.project_name}-fetch"
  role                           = aws_iam_role.lambda.arn
  package_type                   = "Image"
  image_uri                      = var.fetch_lambda_image_uri
  timeout                        = 30
  reserved_concurrent_executions = 1

  environment {
    variables = {
      RAW_SNAPSHOT_QUEUE_URL = aws_sqs_queue.raw_snapshot.url
    }
  }
}

resource "aws_lambda_function" "writer" {
  function_name                  = "${var.project_name}-writer"
  role                           = aws_iam_role.lambda.arn
  package_type                   = "Image"
  image_uri                      = var.writer_lambda_image_uri
  timeout                        = 60
  reserved_concurrent_executions = 1

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      POSTGRES_CONNECTION_STRING = local.postgres_connection_string
    }
  }
}

resource "aws_lambda_function" "read_api" {
  function_name                  = "${var.project_name}-read-api"
  role                           = aws_iam_role.lambda.arn
  package_type                   = "Image"
  image_uri                      = var.read_api_lambda_image_uri
  timeout                        = 30
  reserved_concurrent_executions = 100

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      POSTGRES_CONNECTION_STRING = local.postgres_connection_string
    }
  }
}

resource "aws_lambda_event_source_mapping" "writer_from_sqs" {
  event_source_arn        = aws_sqs_queue.raw_snapshot.arn
  function_name           = aws_lambda_function.writer.arn
  batch_size              = 5
  function_response_types = ["ReportBatchItemFailures"]
}

data "aws_iam_policy_document" "scheduler_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["scheduler.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "scheduler" {
  name               = "${var.project_name}-scheduler"
  assume_role_policy = data.aws_iam_policy_document.scheduler_assume.json
}

resource "aws_iam_role_policy" "scheduler_invoke" {
  role = aws_iam_role.scheduler.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["lambda:InvokeFunction"]
      Resource = aws_lambda_function.fetch.arn
    }]
  })
}

resource "aws_scheduler_schedule" "fetch" {
  name                = "${var.project_name}-fetch"
  schedule_expression = "rate(1 minute)"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.fetch.arn
    role_arn = aws_iam_role.scheduler.arn
  }
}
```

- [ ] **Step 5: Add API Gateway cache and read integration**

Create `infra/terraform/api_gateway.tf`:

```hcl
resource "aws_api_gateway_rest_api" "read_api" {
  name = "${var.project_name}-read-api"
}

resource "aws_api_gateway_resource" "fuel_mix" {
  rest_api_id = aws_api_gateway_rest_api.read_api.id
  parent_id   = aws_api_gateway_rest_api.read_api.root_resource_id
  path_part   = "fuel-mix"
}

resource "aws_api_gateway_resource" "latest" {
  rest_api_id = aws_api_gateway_rest_api.read_api.id
  parent_id   = aws_api_gateway_resource.fuel_mix.id
  path_part   = "latest"
}

resource "aws_api_gateway_method" "latest_get" {
  rest_api_id      = aws_api_gateway_rest_api.read_api.id
  resource_id      = aws_api_gateway_resource.latest.id
  http_method      = "GET"
  authorization    = "NONE"
  api_key_required = true
}

resource "aws_api_gateway_integration" "latest_get" {
  rest_api_id             = aws_api_gateway_rest_api.read_api.id
  resource_id             = aws_api_gateway_resource.latest.id
  http_method             = aws_api_gateway_method.latest_get.http_method
  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.read_api.invoke_arn
}

resource "aws_lambda_permission" "api_gateway_read" {
  statement_id  = "AllowApiGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.read_api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_api_gateway_rest_api.read_api.execution_arn}/*/*"
}

resource "aws_api_gateway_deployment" "read_api" {
  rest_api_id = aws_api_gateway_rest_api.read_api.id
  depends_on  = [aws_api_gateway_integration.latest_get]

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_api_gateway_stage" "prod" {
  rest_api_id           = aws_api_gateway_rest_api.read_api.id
  deployment_id         = aws_api_gateway_deployment.read_api.id
  stage_name            = "prod"
  cache_cluster_enabled = true
  cache_cluster_size    = "0.5"

  method_settings {
    method_path              = "*/*"
    metrics_enabled          = true
    throttling_rate_limit    = 100
    throttling_burst_limit   = 200
    caching_enabled          = true
    cache_ttl_in_seconds     = var.read_api_cache_ttl_seconds
    cache_data_encrypted     = true
  }
}

resource "aws_api_gateway_usage_plan" "external_reader" {
  name = "${var.project_name}-external-reader"

  api_stages {
    api_id = aws_api_gateway_rest_api.read_api.id
    stage  = aws_api_gateway_stage.prod.stage_name
  }

  throttle_settings {
    burst_limit = 200
    rate_limit  = 100
  }
}

resource "aws_api_gateway_api_key" "external_reader" {
  name = "${var.project_name}-external-reader"
}

resource "aws_api_gateway_usage_plan_key" "external_reader" {
  key_id        = aws_api_gateway_api_key.external_reader.id
  key_type      = "API_KEY"
  usage_plan_id = aws_api_gateway_usage_plan.external_reader.id
}
```

- [ ] **Step 6: Add alarms**

Create `infra/terraform/alarms.tf`:

```hcl
resource "aws_cloudwatch_metric_alarm" "dlq_visible_messages" {
  alarm_name          = "${var.project_name}-dlq-visible-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0

  dimensions = {
    QueueName = aws_sqs_queue.raw_snapshot_dlq.name
  }
}

resource "aws_cloudwatch_metric_alarm" "writer_errors" {
  alarm_name          = "${var.project_name}-writer-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 60
  statistic           = "Sum"
  threshold           = 0

  dimensions = {
    FunctionName = aws_lambda_function.writer.function_name
  }
}

resource "aws_cloudwatch_metric_alarm" "read_throttles" {
  alarm_name          = "${var.project_name}-read-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Throttles"
  namespace           = "AWS/Lambda"
  period              = 60
  statistic           = "Sum"
  threshold           = 0

  dimensions = {
    FunctionName = aws_lambda_function.read_api.function_name
  }
}
```

Create `infra/terraform/outputs.tf`:

```hcl
output "raw_snapshot_queue_url" {
  value = aws_sqs_queue.raw_snapshot.url
}

output "read_api_id" {
  value = aws_api_gateway_rest_api.read_api.id
}

output "read_api_key_value" {
  value     = aws_api_gateway_api_key.external_reader.value
  sensitive = true
}
```

- [ ] **Step 7: Run Terraform format and validate**

Run:

```powershell
terraform -chdir=.\infra\terraform fmt
terraform -chdir=.\infra\terraform init
terraform -chdir=.\infra\terraform validate
```

Expected: `terraform validate` reports success when required variables are supplied through `terraform.tfvars`, environment variables, or command-line `-var` arguments.

- [ ] **Step 8: Commit**

Run:

```powershell
git add infra\terraform
git commit -m "feat: add serverless infrastructure definition"
```

Expected: commit succeeds.

## Task 8: Add Evidence And README

**Files:**
- Create: `README.md`
- Create directory: `docs/evidence`

- [ ] **Step 1: Create evidence directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path .\docs\evidence | Out-Null
```

Expected: directory exists.

- [ ] **Step 2: Capture verification commands**

Run:

```powershell
dotnet test .\TecFuelMix.sln *> .\docs\evidence\01-dotnet-test.txt
docker compose ps *> .\docs\evidence\02-local-postgres-status.txt
terraform -chdir=.\infra\terraform validate *> .\docs\evidence\03-terraform-validate.txt
```

Expected: evidence files are created.

- [ ] **Step 3: Add README**

Create `README.md`:

```markdown
# TEC FuelMix Serverless ELT

## Architecture

EventBridge invokes `TecFuelMix.FetchLambda` once per minute. The fetch Lambda calls the public MISO FuelMix API and publishes the raw snapshot to SQS. `TecFuelMix.WriterLambda` consumes SQS messages and idempotently upserts PostgreSQL through RDS Proxy. `TecFuelMix.ReadApiLambda` serves authorized read requests behind API Gateway REST API caching, usage-plan throttling, Lambda reserved concurrency, and RDS Proxy.

## Local verification

```powershell
docker compose up -d db
dotnet test .\TecFuelMix.sln
docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t tec-fuelmix-fetch .
docker build -f .\src\TecFuelMix.WriterLambda\Dockerfile -t tec-fuelmix-writer .
docker build -f .\src\TecFuelMix.ReadApiLambda\Dockerfile -t tec-fuelmix-read-api .
terraform -chdir=.\infra\terraform validate
```

## Scale controls

- SQS buffers fetched snapshots before database writes.
- API Gateway REST API cache absorbs repeated safe GET requests.
- API Gateway usage plans and method throttles cap client traffic.
- Lambda reserved concurrency caps compute fan-out.
- RDS Proxy pools database connections.
- PostgreSQL unique keys enforce idempotency.

## Evidence

Saved verification output lives in `docs/evidence`.
```

- [ ] **Step 4: Commit**

Run:

```powershell
git add README.md docs\evidence
git commit -m "docs: add runbook and verification evidence"
```

Expected: commit succeeds.

## Self-Review Checklist

- [ ] Spec coverage: scheduler, Lambda ingestion, SQS buffering, PostgreSQL, idempotency, safe external read API, cache, RDS Proxy, monitoring surfaces, and Terraform are all covered by tasks.
- [ ] Red-flag scan: no banned deferral markers remain.
- [ ] Type consistency: `FuelMixSnapshot`, `FuelMixReading`, `FuelMixParser`, `FuelMixRepository`, `TecFuelMix.FetchLambda`, `TecFuelMix.WriterLambda`, and `TecFuelMix.ReadApiLambda` names match across tasks.
- [ ] Verification: every task has a runnable command and expected result.

## Execution Options

Plan complete and saved to `planning/2026-06-22-tec-fuelmix-serverless-elt-implementation-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Which approach?
