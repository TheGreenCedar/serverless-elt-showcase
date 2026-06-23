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

    [Fact]
    public void Parse_rejects_mixed_intervals()
    {
        const string json = """
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
                "INTERVALEST": "2026-06-22 11:10:00 AM",
                "CATEGORY": "Battery Storage",
                "ACT": "-420",
                "FUEL_CATEGORY": "Battery Storage  (-420 MW)"
              }
            ]
          }
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() => FuelMixParser.Parse(json));

        Assert.Contains("mixed INTERVALEST", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
