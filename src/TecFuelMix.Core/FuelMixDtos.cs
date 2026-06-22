namespace TecFuelMix.Core;

public sealed record FuelMixSnapshot(
    string SourceRefId,
    DateTime IntervalEst,
    decimal TotalMw,
    string RawPayload,
    IReadOnlyList<FuelMixReading> Readings);

public sealed record FuelMixReading(
    string Category,
    decimal Mw,
    string SourceLabel);
