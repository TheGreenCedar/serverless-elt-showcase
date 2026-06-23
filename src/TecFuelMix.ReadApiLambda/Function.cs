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

    private readonly DataSourceCache _dataSourceCache;

    private readonly record struct HistoryQuery(DateTime From, DateTime To, string? Category, int Limit);

    public Function()
    {
        _dataSourceCache = new DataSourceCache(DatabaseConnectionFactory.CreateAsync);
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSourceCache = new DataSourceCache(dataSource);
    }

    public static Function CreateWithDataSourceFactory(Func<CancellationToken, Task<NpgsqlDataSource>> dataSourceFactory)
    {
        return new Function(dataSourceFactory);
    }

    private Function(Func<CancellationToken, Task<NpgsqlDataSource>> dataSourceFactory)
    {
        _dataSourceCache = new DataSourceCache(dataSourceFactory);
    }

    public APIGatewayCustomAuthorizerResponse Authorize(APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
    {
        var expected = Environment.GetEnvironmentVariable("READ_API_BEARER_TOKEN");
        var actual = BearerToken(request.AuthorizationToken);
        var isAllowed = !string.IsNullOrWhiteSpace(expected) && actual == expected;
        var resource = isAllowed
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
                        Effect = isAllowed ? "Allow" : "Deny",
                        Resource = [resource]
                    }
                ]
            }
        };
    }

    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        Metrics.PushSingleMetric("FuelMixReadRequest", 1, MetricUnit.Count, MetricsNamespace, ServiceName);
        using var timeout = InvocationTimeout.Create(
            context?.RemainingTime ?? TimeSpan.FromSeconds(30),
            TimeoutSafetyBuffer);

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
        var repository = await CreateRepositoryAsync(cancellationToken);
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
        if (!TryReadHistoryQuery(query, out var historyQuery, out var error, out var logInvalidDate))
        {
            if (logInvalidDate)
            {
                Logger.LogInformation("Rejected fuel mix history request with invalid date query.");
            }

            return BadRequest(error);
        }

        var repository = await CreateRepositoryAsync(cancellationToken);
        var rows = await repository.QueryHistoryAsync(
            historyQuery.From,
            historyQuery.To,
            historyQuery.Category,
            historyQuery.Limit,
            cancellationToken);

        return Json(HttpStatusCode.OK, rows);
    }

    private async Task<APIGatewayProxyResponse> Categories(CancellationToken cancellationToken)
    {
        var repository = await CreateRepositoryAsync(cancellationToken);
        var categories = await repository.GetCategoriesAsync(cancellationToken);
        return Json(HttpStatusCode.OK, categories);
    }

    private async Task<APIGatewayProxyResponse> LatestIngestionRun(CancellationToken cancellationToken)
    {
        var repository = await CreateRepositoryAsync(cancellationToken);
        var run = await repository.GetLatestIngestionRunAsync(cancellationToken);
        return run is null
            ? Json(HttpStatusCode.NotFound, new { error = "No ingestion run found." })
            : Json(HttpStatusCode.OK, run);
    }

    private async Task<FuelMixRepository> CreateRepositoryAsync(CancellationToken cancellationToken)
    {
        return new FuelMixRepository(await _dataSourceCache.GetAsync(cancellationToken));
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

    private static APIGatewayProxyResponse BadRequest(string error)
    {
        return Json(HttpStatusCode.BadRequest, new { error });
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

    private static bool TryReadHistoryQuery(
        IDictionary<string, string>? query,
        out HistoryQuery historyQuery,
        out string error,
        out bool logInvalidDate)
    {
        historyQuery = default;
        logInvalidDate = false;
        if (!TryReadDate(query, "from", out var from, out var fromError))
        {
            logInvalidDate = true;
            error = fromError ?? "";
            return false;
        }

        if (!TryReadDate(query, "to", out var to, out var toError))
        {
            logInvalidDate = true;
            error = toError ?? "";
            return false;
        }

        if (to <= from)
        {
            error = "Query parameter 'to' must be after 'from'.";
            return false;
        }

        if (to - from > TimeSpan.FromDays(MaxRangeDays))
        {
            error = $"History range cannot exceed {MaxRangeDays} days.";
            return false;
        }

        var limit = DefaultLimit;
        if (query?.TryGetValue("limit", out var limitText) == true &&
            (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit <= 0))
        {
            error = "Query parameter 'limit' must be a positive integer.";
            return false;
        }

        if (limit > MaxLimit)
        {
            error = $"Query parameter 'limit' cannot exceed {MaxLimit}.";
            return false;
        }

        var category = query?.TryGetValue("category", out var categoryText) == true &&
            !string.IsNullOrWhiteSpace(categoryText)
            ? categoryText.Trim()
            : null;

        historyQuery = new HistoryQuery(from, to, category, limit);
        error = "";
        return true;
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

}
