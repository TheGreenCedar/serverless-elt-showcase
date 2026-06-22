using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.ReadApiLambda;

public sealed class Function
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int MaxRangeDays = 7;
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

    private readonly NpgsqlDataSource _dataSource;

    public Function()
        : this(NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? ""))
    {
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        using var timeout = CreateInvocationTimeout(context);

        return (request.HttpMethod?.ToUpperInvariant(), NormalizePath(request.Path)) switch
        {
            ("GET", "/fuel-mix/latest") => await Latest(timeout.Token),
            ("GET", "/fuel-mix") => await History(request.QueryStringParameters, timeout.Token),
            ("GET", "/fuel-mix/categories") => await Categories(timeout.Token),
            ("GET", "/ingestion-runs/latest") => await LatestIngestionRun(timeout.Token),
            ("GET", "/health") => Json(HttpStatusCode.OK, new { status = "ok" }),
            _ => Json(HttpStatusCode.NotFound, new { error = "Route not found." })
        };
    }

    private async Task<APIGatewayProxyResponse> Latest(CancellationToken cancellationToken)
    {
        var snapshot = await Repository().GetLatestSnapshotAsync(cancellationToken);
        return snapshot is null
            ? Json(HttpStatusCode.NotFound, new { error = "No fuel mix snapshot found." })
            : Json(HttpStatusCode.OK, snapshot);
    }

    private async Task<APIGatewayProxyResponse> History(
        IDictionary<string, string>? query,
        CancellationToken cancellationToken)
    {
        if (!TryReadDate(query, "from", out var from, out var error) ||
            !TryReadDate(query, "to", out var to, out error))
        {
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
        var rows = await Repository().QueryHistoryAsync(
            from,
            to,
            string.IsNullOrWhiteSpace(category) ? null : category,
            limit,
            cancellationToken);

        return Json(HttpStatusCode.OK, rows);
    }

    private async Task<APIGatewayProxyResponse> Categories(CancellationToken cancellationToken)
    {
        var categories = await Repository().GetCategoriesAsync(cancellationToken);
        return Json(HttpStatusCode.OK, categories);
    }

    private async Task<APIGatewayProxyResponse> LatestIngestionRun(CancellationToken cancellationToken)
    {
        var run = await Repository().GetLatestIngestionRunAsync(cancellationToken);
        return run is null
            ? Json(HttpStatusCode.NotFound, new { error = "No ingestion run found." })
            : Json(HttpStatusCode.OK, run);
    }

    private FuelMixRepository Repository() => new(_dataSource);

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

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            error = $"Query parameter '{name}' must be a valid date.";
            return false;
        }

        error = null;
        return true;
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
