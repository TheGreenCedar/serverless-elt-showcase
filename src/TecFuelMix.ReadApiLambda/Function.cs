using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.ReadApiLambda;

public sealed record QueryOptions(DateTime? FromUtc, DateTime? ToUtc, string? Category, int Limit)
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    public static QueryOptions From(IReadOnlyDictionary<string, string>? query)
    {
        if (query is null)
        {
            return new QueryOptions(null, null, null, DefaultLimit);
        }

        var from = ParseOptionalDateTime(query, "from");
        var to = ParseOptionalDateTime(query, "to");
        if (from.HasValue && to.HasValue && to.Value - from.Value > MaxRange)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Date ranges over 7 days are not supported.");
        }

        var category = query.TryGetValue("category", out var categoryValue) && !string.IsNullOrWhiteSpace(categoryValue)
            ? categoryValue.Trim()
            : null;

        var limit = DefaultLimit;
        if (query.TryGetValue("limit", out var limitValue) && int.TryParse(limitValue, out var parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 1, MaxLimit);
        }

        return new QueryOptions(from, to, category, limit);
    }

    private static DateTime? ParseOptionalDateTime(IReadOnlyDictionary<string, string> query, string key)
    {
        if (!query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            return parsed;
        }

        throw new ArgumentOutOfRangeException(key, $"Invalid {key} value.");
    }
}

public sealed class Function
{
    private const string LatestPath = "/fuel-mix/latest";
    private static readonly TimeSpan TimeoutSafetyBuffer = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);
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
        try
        {
            if (!IsLatestPath(request.Path))
            {
                return JsonResponse(404, new { message = "Not found" });
            }

            var query = request.QueryStringParameters is null
                ? null
                : new Dictionary<string, string>(request.QueryStringParameters);
            _ = QueryOptions.From(query);

            using var timeout = CreateInvocationTimeout(context);
            var repository = new FuelMixRepository(_dataSource);
            var snapshot = await repository.GetLatestSnapshotAsync(timeout.Token);
            if (snapshot is null)
            {
                return JsonResponse(404, new { message = "No fuel mix snapshot found" });
            }

            return JsonResponse(200, new
            {
                refId = snapshot.SourceRefId,
                intervalEst = snapshot.IntervalEst,
                totalMw = snapshot.TotalMw,
                fuels = snapshot.Readings.Select(reading => new
                {
                    category = reading.Category,
                    mw = reading.Mw
                })
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return JsonResponse(400, new { message = ex.Message });
        }
    }

    private static APIGatewayProxyResponse JsonResponse(int statusCode, object body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            },
            Body = JsonSerializer.Serialize(body, ResponseJsonOptions)
        };
    }

    private static bool IsLatestPath(string? path)
    {
        return path?.EndsWith(LatestPath, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static CancellationTokenSource CreateInvocationTimeout(ILambdaContext context)
    {
        var timeout = new CancellationTokenSource();
        var remaining = context.RemainingTime;

        if (remaining <= TimeoutSafetyBuffer)
        {
            timeout.Cancel();
            return timeout;
        }

        timeout.CancelAfter(remaining - TimeoutSafetyBuffer);
        return timeout;
    }
}
