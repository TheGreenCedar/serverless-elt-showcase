using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using TecFuelMix.Core;
using TecFuelMix.ReadApiLambda;

namespace TecFuelMix.Tests;

public sealed class ReadApiValidationTests
{
    [Fact]
    public async Task Latest_returns_ok_when_snapshot_exists()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(Request(
            "/fuel-mix/latest",
            new Dictionary<string, string>
            {
                ["ignored"] = "ok"
            }), null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains(snapshot.SourceRefId, response.Body);
        Assert.Contains("readings", response.Body);
    }

    [Fact]
    public async Task History_rejects_missing_dates()
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request("/fuel-mix"), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("from", response.Body);
    }

    [Theory]
    [InlineData("from", "not-a-date")]
    [InlineData("to", "still-not-a-date")]
    public async Task History_rejects_invalid_date_strings(string key, string value)
    {
        var function = FunctionWithoutDatabase();
        var query = new Dictionary<string, string>
        {
            ["from"] = "2026-06-01T00:00:00",
            ["to"] = "2026-06-02T00:00:00"
        };
        query[key] = value;

        var response = await function.Handler(Request("/fuel-mix", query), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(key, response.Body);
        Assert.Contains("source-local date", response.Body);
    }

    [Theory]
    [InlineData("2026-06-01T00:00:00Z")]
    [InlineData("2026-06-01T00:00:00-05:00")]
    [InlineData("2026-06-01T00:00:00+00:00")]
    public async Task History_rejects_offset_dates(string from)
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request(
            "/fuel-mix",
            new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = "2026-06-02T00:00:00"
            }), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("source-local", response.Body);
    }

    [Theory]
    [InlineData("2026-06-01T00:00:00")]
    [InlineData("2026-05-31T23:59:59")]
    public async Task History_rejects_to_before_or_equal_from(string to)
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request(
            "/fuel-mix",
            new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00",
                ["to"] = to
            }), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("after", response.Body);
    }

    [Fact]
    public async Task History_rejects_range_over_seven_days()
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request(
            "/fuel-mix",
            new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00",
                ["to"] = "2026-06-09T00:00:00"
            }), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("7 days", response.Body);
    }

    [Fact]
    public async Task History_rejects_limit_over_500()
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request(
            "/fuel-mix",
            new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00",
                ["to"] = "2026-06-02T00:00:00",
                ["limit"] = "501"
            }), null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("500", response.Body);
    }

    [Fact]
    public async Task History_uses_default_limit_100()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
        var repository = new FuelMixRepository(dataSource);
        var seed = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        var start = new DateTime(2026, 6, 1, 0, 0, 0);
        for (var i = 0; i < 101; i++)
        {
            await repository.UpsertSnapshotAsync(seed with
            {
                SourceRefId = $"snapshot-{i:D3}",
                IntervalEst = start.AddMinutes(i),
                TotalMw = i,
                Readings =
                [
                    new FuelMixReading("Coal", i, $"Coal  ({i} MW)")
                ]
            }, CancellationToken.None);
        }

        var response = await new Function(dataSource).Handler(Request(
            "/fuel-mix",
            new Dictionary<string, string>
            {
                ["from"] = start.AddMinutes(-1).ToString("O"),
                ["to"] = start.AddMinutes(102).ToString("O")
            }), null!);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(100, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Categories_returns_ok()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(Request("/fuel-mix/categories"), null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("Battery Storage", response.Body);
        Assert.Contains("Coal", response.Body);
    }

    [Fact]
    public async Task LatestIngestionRun_returns_ok()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        await new FuelMixRepository(dataSource).UpsertSnapshotAsync(snapshot, CancellationToken.None);
        var function = new Function(dataSource);

        var response = await function.Handler(Request("/ingestion-runs/latest"), null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("succeeded", response.Body);
        Assert.Contains(snapshot.SourceRefId, response.Body);
    }

    [Fact]
    public async Task Health_returns_ok_without_database_query()
    {
        var function = FunctionWithoutDatabase();

        var response = await function.Handler(Request("/health"), null!);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("ok", response.Body);
    }

    [Fact]
    public void Authorize_allows_matching_bearer_token()
    {
        var function = FunctionWithoutDatabase();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            AuthorizationToken = "Bearer test-token",
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var env = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");

        var response = function.Authorize(request, null!);

        Assert.Equal("Allow", Effect(response));
    }

    [Fact]
    public void Authorize_with_default_constructor_does_not_require_database_environment()
    {
        var function = new Function();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            AuthorizationToken = "Bearer test-token",
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var token = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");
        using var connectionString = new EnvironmentVariableScope("POSTGRES_CONNECTION_STRING", null);
        using var host = new EnvironmentVariableScope("POSTGRES_HOST", null);
        using var database = new EnvironmentVariableScope("POSTGRES_DATABASE", null);
        using var secretArn = new EnvironmentVariableScope("POSTGRES_SECRET_ARN", null);

        var response = function.Authorize(request, null!);

        Assert.Equal("Allow", Effect(response));
    }

    [Fact]
    public void Authorize_denies_missing_token()
    {
        var function = FunctionWithoutDatabase();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var env = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");

        var response = function.Authorize(request, null!);

        Assert.Equal("Deny", Effect(response));
    }

    [Fact]
    public void Authorize_denies_wrong_token()
    {
        var function = FunctionWithoutDatabase();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            AuthorizationToken = "Bearer wrong-token",
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var env = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");

        var response = function.Authorize(request, null!);

        Assert.Equal("Deny", Effect(response));
    }

    [Fact]
    public void Authorize_denies_token_without_bearer_scheme()
    {
        var function = FunctionWithoutDatabase();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            AuthorizationToken = "test-token",
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var env = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");

        var response = function.Authorize(request, null!);

        Assert.Equal("Deny", Effect(response));
    }

    private static string Effect(APIGatewayCustomAuthorizerResponse response)
    {
        return response.PolicyDocument.Statement.Single().Effect;
    }

    [Fact]
    public void Authorize_allows_read_route_set_for_cached_tokens()
    {
        var function = FunctionWithoutDatabase();
        var request = new APIGatewayCustomAuthorizerRequest
        {
            AuthorizationToken = "Bearer test-token",
            MethodArn = "arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/fuel-mix/latest"
        };

        using var env = new EnvironmentVariableScope("READ_API_BEARER_TOKEN", "test-token");

        var response = function.Authorize(request, null!);

        Assert.Equal("arn:aws:execute-api:us-east-1:123456789012:api/prod/GET/*", Resource(response));
    }

    private static string Resource(APIGatewayCustomAuthorizerResponse response)
    {
        return response.PolicyDocument.Statement.Single().Resource.Single();
    }

    private static APIGatewayProxyRequest Request(
        string path,
        IDictionary<string, string>? query = null)
    {
        return new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = path,
            QueryStringParameters = query
        };
    }

    private static Function FunctionWithoutDatabase()
    {
        return Function.CreateWithDataSourceFactory(_ =>
            throw new InvalidOperationException("Database should not be used."));
    }

    [Fact]
    public async Task Handler_returns_503_for_dependency_failure()
    {
        var function = Function.CreateWithDataSourceFactory(_ => throw new TimeoutException("database unavailable"));

        var response = await function.Handler(Request("/fuel-mix/latest"), null!);

        Assert.Equal(503, response.StatusCode);
        Assert.Contains("unavailable", response.Body);
    }

    [Fact]
    public async Task Handler_returns_404_for_unknown_route()
    {
        var function = FunctionWithoutDatabase();
        var request = Request("/fuel-mix/unknown");

        var response = await function.Handler(request, null!);

        Assert.Equal(404, response.StatusCode);
        Assert.Contains("Not found", response.Body);
    }
}
