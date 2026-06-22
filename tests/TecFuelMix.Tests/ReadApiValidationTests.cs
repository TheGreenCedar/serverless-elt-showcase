using Amazon.Lambda.APIGatewayEvents;
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

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    public void QueryOptions_rejects_malformed_date_values(string key)
    {
        var query = new Dictionary<string, string> { [key] = "not-a-date" };

        Assert.Throws<ArgumentOutOfRangeException>(() => QueryOptions.From(query));
    }

    [Fact]
    public async Task Handler_returns_400_for_malformed_date_query()
    {
        var function = new Function(null!);
        var request = new APIGatewayProxyRequest
        {
            Path = "/fuel-mix/latest",
            QueryStringParameters = new Dictionary<string, string>
            {
                ["from"] = "not-a-date"
            }
        };

        var response = await function.Handler(request, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("Invalid from value.", response.Body);
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
                ["from"] = "not-a-date"
            }
        };

        var response = await function.Handler(request, null!);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("Invalid from value.", response.Body);
    }
}
