namespace TecFuelMix.Tests;

internal static class SamplePayloads
{
    public const string FuelMixJson = """
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
            "INTERVALEST": "2026-06-22 11:05:00 AM",
            "CATEGORY": "Battery Storage",
            "ACT": "-420",
            "FUEL_CATEGORY": "Battery Storage  (-420 MW)"
          }
        ]
      }
    }
    """;
}
