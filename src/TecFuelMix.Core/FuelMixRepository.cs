using Npgsql;

namespace TecFuelMix.Core;

public sealed class FuelMixRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public FuelMixRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<FuelMixSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var snapshotCommand = new NpgsqlCommand("""
            select id, source_ref_id, interval_est, total_mw, raw_payload::text
            from fuel_mix_snapshots
            order by interval_est desc
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
        var rawPayload = snapshotReader.GetString(4);
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

        return new FuelMixSnapshot(sourceRefId, intervalEst, totalMw, rawPayload, readings);
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
