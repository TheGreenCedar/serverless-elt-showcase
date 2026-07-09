using System.Globalization;
using System.Text.Json;

namespace TecFuelMix.Core;

public static class FuelMixParser
{
    public static FuelMixSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var refId = RequiredString(root, "RefId");
        var totalMw = ParseDecimal(RequiredString(root, "TotalMW"), "TotalMW");
        var rows = root.GetProperty("Fuel").GetProperty("Type").EnumerateArray().ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Fuel.Type contains no readings.");
        }

        var interval = ParseInterval(RequiredString(rows[0], "INTERVALEST"));
        var readings = new FuelMixReading[rows.Length];

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var rowInterval = ParseInterval(RequiredString(row, "INTERVALEST"));
            if (rowInterval != interval)
            {
                throw new InvalidOperationException("Fuel.Type contains mixed INTERVALEST values.");
            }

            readings[i] = new FuelMixReading(
                RequiredString(row, "CATEGORY"),
                ParseDecimal(RequiredString(row, "ACT"), "ACT"),
                RequiredString(row, "FUEL_CATEGORY"));
        }

        return new FuelMixSnapshot(refId, interval, totalMw, json, readings);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Missing required string field '{name}'.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Missing required string field '{name}'.");
        }

        return text;
    }

    private static decimal ParseDecimal(string text, string fieldName)
    {
        var normalized = text.Replace(",", "", StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Field '{fieldName}' is not a decimal value.");
        }

        return value;
    }

    private static DateTime ParseInterval(string text)
    {
        if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd h:mm:ss tt",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            throw new InvalidOperationException("INTERVALEST is not in the expected source format.");
        }

        return value;
    }
}
