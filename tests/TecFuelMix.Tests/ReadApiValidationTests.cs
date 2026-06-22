using Amazon.Lambda.APIGatewayEvents;
using TecFuelMix.ReadApiLambda;

namespace TecFuelMix.Tests;

public sealed class ReadApiValidationTests
{
    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("category")]
    [InlineData("limit")]
    public async Task Handler_returns_400_for_latest_query_parameters(string key)
    {
        var function = new Function(null!);
        var request = new APIGatewayProxyRequest
        {
            Path = "/fuel-mix/latest",
            QueryStringParameters = new Dictionary<string, string>
            {
                [key] = "ignored"
            }
        };

        var response = await function.Handler(request, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("/fuel-mix/latest does not support query parameters.", response.Body);
    }

    [Fact]
    public async Task Handler_matches_latest_route_with_stage_prefix()
    {
        var function = new Function(null!);
        var request = new APIGatewayProxyRequest
        {
            Path = "/prod/fuel-mix/latest",
            QueryStringParameters = new Dictionary<string, string>
            {
                ["from"] = "2026-06-01T00:00:00"
            }
        };

        var response = await function.Handler(request, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("/fuel-mix/latest does not support query parameters.", response.Body);
    }
}
