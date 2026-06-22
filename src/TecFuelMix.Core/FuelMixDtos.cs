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

public sealed record FuelMixSnapshotResponse(
    string SourceRefId,
    DateTime IntervalEst,
    decimal TotalMw,
    IReadOnlyList<FuelMixReading> Readings);

public sealed record FuelMixHistoryRow(
    string SourceRefId,
    DateTime IntervalEst,
    string Category,
    decimal Mw,
    string SourceLabel);

public sealed record IngestionRunStatus(
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Status,
    string? SourceRefId,
    string? ErrorMessage);
