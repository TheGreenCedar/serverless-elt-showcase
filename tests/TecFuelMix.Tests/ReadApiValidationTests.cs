using Amazon.Lambda.APIGatewayEvents;
using Npgsql;
using TecFuelMix.Core;
using TecFuelMix.ReadApiLambda;

namespace TecFuelMix.Tests;

public sealed class ReadApiValidationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password";

    [Fact]
    public async Task Latest_returns_ok_when_snapshot_exists()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/fuel-mix/latest",
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ignored"] = "ok"
            }
        }, null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains(snapshot.SourceRefId, response.Body);
        Assert.Contains("readings", response.Body);
    }

    [Fact]
    public async Task History_rejects_missing_dates()
    {
        var function = new Function(null!);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/fuel-mix"
        }, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("from", response.Body);
    }

    [Fact]
    public async Task History_rejects_range_over_seven_days()
    {
        var function = new Function(null!);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/fuel-mix",
            QueryStringParameters = new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00",
                ["to"] = "2026-06-09T00:00:00"
            }
        }, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("7 days", response.Body);
    }

    [Fact]
    public async Task History_rejects_limit_over_500()
    {
        var function = new Function(null!);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/fuel-mix",
            QueryStringParameters = new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00",
                ["to"] = "2026-06-02T00:00:00",
                ["limit"] = "501"
            }
        }, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("500", response.Body);
    }

    [Fact]
    public async Task Categories_returns_ok()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/fuel-mix/categories"
        }, null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("Battery Storage", response.Body);
        Assert.Contains("Coal", response.Body);
    }

    [Fact]
    public async Task LatestIngestionRun_returns_ok()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/ingestion-runs/latest"
        }, null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("succeeded", response.Body);
        Assert.Contains(snapshot.SourceRefId, response.Body);
    }

    [Fact]
    public async Task Health_returns_ok_without_database_query()
    {
        var function = new Function(null!);

        var response = await function.Handler(new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/health"
        }, null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("ok", response.Body);
    }

    private static async Task ResetDatabase(NpgsqlDataSource dataSource)
    {
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "TecFuelMix.Core",
            "Schema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath);
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
