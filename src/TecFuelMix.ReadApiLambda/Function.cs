using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.ReadApiLambda;

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
            if (query?.Count > 0)
            {
                return JsonResponse(400, new { message = "/fuel-mix/latest does not support query parameters." });
            }

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
