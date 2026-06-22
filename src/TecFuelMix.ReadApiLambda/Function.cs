using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Runtime;
using AWS.Lambda.Powertools.Logging;
using AWS.Lambda.Powertools.Metrics;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.ReadApiLambda;

public sealed class Function
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int MaxRangeDays = 7;
    private const string MetricsNamespace = "TecFuelMix";
    private const string ServiceName = "ReadApiLambda";
    private static readonly string[] SourceLocalDateFormats =
    [
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss"
    ];
    private static readonly TimeSpan TimeoutSafetyBuffer = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] KnownRoutes =
    [
        "/fuel-mix/latest",
        "/fuel-mix",
        "/fuel-mix/categories",
        "/ingestion-runs/latest",
        "/health"
    ];

    private readonly Func<CancellationToken, Task<NpgsqlDataSource>> _dataSourceFactory;
    private readonly SemaphoreSlim _dataSourceLock = new(1, 1);
    private NpgsqlDataSource? _dataSource;

    public Function()
    {
        _dataSourceFactory = DatabaseConnectionFactory.CreateAsync;
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _dataSourceFactory = _ => Task.FromResult(dataSource);
    }

    public static Function CreateWithDataSourceFactory(Func<CancellationToken, Task<NpgsqlDataSource>> dataSourceFactory)
    {
        return new Function(dataSourceFactory);
    }

    private Function(Func<CancellationToken, Task<NpgsqlDataSource>> dataSourceFactory)
    {
        _dataSourceFactory = dataSourceFactory;
    }

    public APIGatewayCustomAuthorizerResponse Authorize(APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
    {
        var expected = Environment.GetEnvironmentVariable("READ_API_BEARER_TOKEN");
        var actual = BearerToken(request.AuthorizationToken);
        var effect = !string.IsNullOrWhiteSpace(expected) && actual == expected ? "Allow" : "Deny";
        var resource = effect == "Allow"
            ? ReadApiRouteSetArn(request.MethodArn)
            : request.MethodArn;

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
                        Resource = [resource]
                    }
                ]
            }
        };
    }

    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        Metrics.PushSingleMetric("FuelMixReadRequest", 1, MetricUnit.Count, MetricsNamespace, ServiceName);
        using var timeout = CreateInvocationTimeout(context);

        try
        {
            var route = NormalizePath(request.Path);
            return (request.HttpMethod?.ToUpperInvariant(), route) switch
            {
                ("GET", "/fuel-mix/latest") => await Latest(timeout.Token),
                ("GET", "/fuel-mix") => await History(request.QueryStringParameters, timeout.Token),
                ("GET", "/fuel-mix/categories") => await Categories(timeout.Token),
                ("GET", "/ingestion-runs/latest") => await LatestIngestionRun(timeout.Token),
                ("GET", "/health") => Json(HttpStatusCode.OK, new { status = "ok" }),
                _ => NotFound()
            };
        }
        catch (Exception ex) when (IsDependencyFailure(ex, timeout.Token))
        {
            Metrics.PushSingleMetric("FuelMixReadFailed", 1, MetricUnit.Count, MetricsNamespace, ServiceName);
            Logger.LogError("Read API dependency failure with {ErrorType}.", ex.GetType().Name);
            return Json(HttpStatusCode.ServiceUnavailable, new { error = "Read API dependency is unavailable." });
        }
    }

    private static bool IsDependencyFailure(Exception ex, CancellationToken cancellationToken)
    {
        return ex is AmazonServiceException or NpgsqlException or TimeoutException ||
            ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }

    private async Task<APIGatewayProxyResponse> Latest(CancellationToken cancellationToken)
    {
        var repository = new FuelMixRepository(await GetDataSourceAsync(cancellationToken));
        var snapshot = await repository.GetLatestSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            Logger.LogInformation("No fuel mix snapshot found for latest request.");
            return Json(HttpStatusCode.NotFound, new { error = "No fuel mix snapshot found." });
        }

        Metrics.PushSingleMetric(
            "FuelMixLatestSnapshotAgeSeconds",
            GetSnapshotAgeSeconds(snapshot.IntervalEst),
            MetricUnit.Seconds,
            MetricsNamespace,
            ServiceName);
        Logger.LogInformation("Returned latest fuel mix snapshot {SourceRefId}.", snapshot.SourceRefId);

        return Json(HttpStatusCode.OK, snapshot);
    }

    private async Task<APIGatewayProxyResponse> History(
        IDictionary<string, string>? query,
        CancellationToken cancellationToken)
    {
        if (!TryReadDate(query, "from", out var from, out var error) ||
            !TryReadDate(query, "to", out var to, out error))
        {
            Logger.LogInformation("Rejected fuel mix history request with invalid date query.");
            return Json(HttpStatusCode.BadRequest, new { error });
        }

        if (to <= from)
        {
            return Json(HttpStatusCode.BadRequest, new { error = "Query parameter 'to' must be after 'from'." });
        }

        if (to - from > TimeSpan.FromDays(MaxRangeDays))
        {
            return Json(HttpStatusCode.BadRequest, new { error = $"History range cannot exceed {MaxRangeDays} days." });
        }

        var limit = DefaultLimit;
        if (query?.TryGetValue("limit", out var limitText) == true &&
            (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit <= 0))
        {
            return Json(HttpStatusCode.BadRequest, new { error = "Query parameter 'limit' must be a positive integer." });
        }

        if (limit > MaxLimit)
        {
            return Json(HttpStatusCode.BadRequest, new { error = $"Query parameter 'limit' cannot exceed {MaxLimit}." });
        }

        var category = query?.TryGetValue("category", out var categoryText) == true
            ? categoryText.Trim()
            : null;
        var repository = new FuelMixRepository(await GetDataSourceAsync(cancellationToken));
        var rows = await repository.QueryHistoryAsync(
            from,
            to,
            string.IsNullOrWhiteSpace(category) ? null : category,
            limit,
            cancellationToken);

        return Json(HttpStatusCode.OK, rows);
    }

    private async Task<APIGatewayProxyResponse> Categories(CancellationToken cancellationToken)
    {
        var repository = new FuelMixRepository(await GetDataSourceAsync(cancellationToken));
        var categories = await repository.GetCategoriesAsync(cancellationToken);
        return Json(HttpStatusCode.OK, categories);
    }

    private async Task<APIGatewayProxyResponse> LatestIngestionRun(CancellationToken cancellationToken)
    {
        var repository = new FuelMixRepository(await GetDataSourceAsync(cancellationToken));
        var run = await repository.GetLatestIngestionRunAsync(cancellationToken);
        return run is null
            ? Json(HttpStatusCode.NotFound, new { error = "No ingestion run found." })
            : Json(HttpStatusCode.OK, run);
    }

    private async Task<NpgsqlDataSource> GetDataSourceAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is { } dataSource)
        {
            return dataSource;
        }

        await _dataSourceLock.WaitAsync(cancellationToken);
        try
        {
            if (_dataSource is { } cachedDataSource)
            {
                return cachedDataSource;
            }

            dataSource = await _dataSourceFactory(cancellationToken);
            _dataSource = dataSource;
            return dataSource;
        }
        finally
        {
            _dataSourceLock.Release();
        }
    }

    private static string? BearerToken(string? authorizationToken)
    {
        const string prefix = "Bearer ";
        return authorizationToken?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? authorizationToken[prefix.Length..]
            : null;
    }

    private static string ReadApiRouteSetArn(string methodArn)
    {
        var arnParts = methodArn.Split(':', 6);
        if (arnParts.Length != 6)
        {
            return methodArn;
        }

        var resourceParts = arnParts[5].Split('/', 4);
        if (resourceParts.Length < 2)
        {
            return methodArn;
        }

        var arnPrefix = string.Join(':', arnParts[..5]);
        return $"{arnPrefix}:{resourceParts[0]}/{resourceParts[1]}/GET/*";
    }

    private static APIGatewayProxyResponse NotFound()
    {
        Logger.LogInformation("Read API route not found.");
        return Json(HttpStatusCode.NotFound, new { error = "Not found." });
    }

    private static APIGatewayProxyResponse Json(HttpStatusCode statusCode, object body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)statusCode,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            },
            Body = JsonSerializer.Serialize(body, ResponseJsonOptions)
        };
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.Split('?', 2)[0].TrimEnd('/');
        if (normalized.Length == 0)
        {
            return "/";
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        foreach (var route in KnownRoutes)
        {
            if (normalized.Equals(route, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(route, StringComparison.OrdinalIgnoreCase))
            {
                return route;
            }
        }

        return normalized.ToLowerInvariant();
    }

    private static bool TryReadDate(
        IDictionary<string, string>? query,
        string name,
        out DateTime value,
        out string? error)
    {
        value = default;
        if (query is null || !query.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            error = $"Query parameter '{name}' is required.";
            return false;
        }

        if (!DateTime.TryParseExact(
                text,
                SourceLocalDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value))
        {
            error = $"Query parameter '{name}' must be a valid source-local date.";
            return false;
        }

        error = null;
        return true;
    }

    private static double GetSnapshotAgeSeconds(DateTime intervalEst)
    {
        var interval = new DateTimeOffset(DateTime.SpecifyKind(intervalEst, DateTimeKind.Unspecified), TimeSpan.FromHours(-5));
        return Math.Max(0, (DateTimeOffset.UtcNow - interval).TotalSeconds);
    }

    private static CancellationTokenSource CreateInvocationTimeout(ILambdaContext? context)
    {
        var timeout = new CancellationTokenSource();
        var remaining = context?.RemainingTime ?? TimeSpan.FromSeconds(30);

        if (remaining <= TimeoutSafetyBuffer)
        {
            timeout.Cancel();
            return timeout;
        }

        timeout.CancelAfter(remaining - TimeoutSafetyBuffer);
        return timeout;
    }
}
