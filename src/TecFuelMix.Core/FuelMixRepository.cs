using Npgsql;
using NpgsqlTypes;

namespace TecFuelMix.Core;

public sealed class FuelMixRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public FuelMixRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<FuelMixSnapshotResponse?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var snapshotCommand = new NpgsqlCommand("""
            select id, source_ref_id, interval_est, total_mw
            from fuel_mix_snapshots
            order by interval_est desc, id desc
            limit 1;
            """, connection);

        await using var snapshotReader = await snapshotCommand.ExecuteReaderAsync(cancellationToken);
        if (!await snapshotReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var snapshotId = snapshotReader.GetInt64(0);
        var sourceRefId = snapshotReader.GetString(1);
        var intervalEst = snapshotReader.GetDateTime(2);
        var totalMw = snapshotReader.GetDecimal(3);
        await snapshotReader.CloseAsync();

        var readings = new List<FuelMixReading>();
        await using var readingsCommand = new NpgsqlCommand("""
            select category, mw, source_label
            from fuel_mix_readings
            where snapshot_id = @snapshot_id
            order by category;
            """, connection);
        readingsCommand.Parameters.AddWithValue("snapshot_id", snapshotId);

        await using var readingsReader = await readingsCommand.ExecuteReaderAsync(cancellationToken);
        while (await readingsReader.ReadAsync(cancellationToken))
        {
            readings.Add(new FuelMixReading(
                readingsReader.GetString(0),
                readingsReader.GetDecimal(1),
                readingsReader.GetString(2)));
        }

        return new FuelMixSnapshotResponse(sourceRefId, intervalEst, totalMw, readings);
    }

    public async Task<IReadOnlyList<FuelMixHistoryRow>> QueryHistoryAsync(
        DateTime from,
        DateTime to,
        string? category,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("""
            select s.source_ref_id, s.interval_est, r.category, r.mw, r.source_label
            from fuel_mix_snapshots s
            inner join fuel_mix_readings r on r.snapshot_id = s.id
            where s.interval_est >= @from
              and s.interval_est < @to
              and (@category is null or r.category = @category)
            order by s.interval_est desc, r.category
            limit @limit;
            """);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.Add("category", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(category) ? DBNull.Value : category;
        command.Parameters.AddWithValue("limit", limit);

        var rows = new List<FuelMixHistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FuelMixHistoryRow(
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("""
            select distinct category
            from fuel_mix_readings
            order by category;
            """);

        var categories = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(reader.GetString(0));
        }

        return categories;
    }

    public async Task<IngestionRunStatus?> GetLatestIngestionRunAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("""
            select started_at, completed_at, status, source_ref_id, error_message
            from ingestion_runs
            order by started_at desc, id desc
            limit 1;
            """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new IngestionRunStatus(
            reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task<long> UpsertSnapshotAsync(FuelMixSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var snapshotId = await UpsertSnapshotRow(connection, transaction, snapshot, cancellationToken);
        await DeleteReadings(connection, transaction, snapshotId, cancellationToken);
        foreach (var reading in snapshot.Readings)
        {
            await UpsertReadingRow(connection, transaction, snapshotId, reading, cancellationToken);
        }

        await using var run = new NpgsqlCommand("""
            insert into ingestion_runs (completed_at, status, source_ref_id)
            values (now(), 'succeeded', @source_ref_id);
            """, connection, transaction);
        run.Parameters.AddWithValue("source_ref_id", snapshot.SourceRefId);
        await run.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return snapshotId;
    }

    private static async Task<long> UpsertSnapshotRow(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FuelMixSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into fuel_mix_snapshots (source_ref_id, interval_est, total_mw, raw_payload)
            values (@source_ref_id, @interval_est, @total_mw, @raw_payload::jsonb)
            on conflict (source_ref_id)
            do update set
                interval_est = excluded.interval_est,
                total_mw = excluded.total_mw,
                raw_payload = excluded.raw_payload
            returning id;
            """, connection, transaction);
        command.Parameters.AddWithValue("source_ref_id", snapshot.SourceRefId);
        command.Parameters.AddWithValue("interval_est", snapshot.IntervalEst);
        command.Parameters.AddWithValue("total_mw", snapshot.TotalMw);
        command.Parameters.AddWithValue("raw_payload", snapshot.RawPayload);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static async Task DeleteReadings(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long snapshotId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            delete from fuel_mix_readings
            where snapshot_id = @snapshot_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("snapshot_id", snapshotId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertReadingRow(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long snapshotId,
        FuelMixReading reading,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into fuel_mix_readings (snapshot_id, category, mw, source_label)
            values (@snapshot_id, @category, @mw, @source_label)
            on conflict (snapshot_id, category)
            do update set
                mw = excluded.mw,
                source_label = excluded.source_label;
            """, connection, transaction);
        command.Parameters.AddWithValue("snapshot_id", snapshotId);
        command.Parameters.AddWithValue("category", reading.Category);
        command.Parameters.AddWithValue("mw", reading.Mw);
        command.Parameters.AddWithValue("source_label", reading.SourceLabel);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
