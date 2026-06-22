using Npgsql;
using TecFuelMix.Core;

namespace TecFuelMix.Tests;

public sealed class FuelMixRepositoryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public FuelMixRepositoryTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task UpsertSnapshotAsync_is_idempotent_by_source_ref_and_category()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);

        await repository.UpsertSnapshotAsync(snapshot, CancellationToken.None);
        await repository.UpsertSnapshotAsync(snapshot, CancellationToken.None);

        await using var command = dataSource.CreateCommand("""
            select
                (select count(*) from fuel_mix_snapshots) as snapshot_count,
                (select count(*) from fuel_mix_readings) as reading_count
            """);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [Fact]
    public async Task UpsertSnapshotAsync_removes_readings_absent_from_replacement_snapshot()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var original = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        var replacement = original with
        {
            Readings = new[]
            {
                new FuelMixReading("Coal", 27000m, "Coal  (27,000 MW)")
            }
        };

        var snapshotId = await repository.UpsertSnapshotAsync(original, CancellationToken.None);
        var replacementSnapshotId = await repository.UpsertSnapshotAsync(replacement, CancellationToken.None);

        Assert.Equal(snapshotId, replacementSnapshotId);

        await using var command = dataSource.CreateCommand("""
            select category, mw, source_label
            from fuel_mix_readings
            where snapshot_id = @snapshot_id
            order by category;
            """);
        command.Parameters.AddWithValue("snapshot_id", snapshotId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("Coal", reader.GetString(0));
        Assert.Equal(27000m, reader.GetDecimal(1));
        Assert.Equal("Coal  (27,000 MW)", reader.GetString(2));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task GetLatestSnapshotAsync_returns_newest_snapshot()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var older = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        var newer = older with
        {
            SourceRefId = "22-Jun-2026 - Interval 11:10 EST",
            IntervalEst = older.IntervalEst.AddMinutes(5)
        };

        await repository.UpsertSnapshotAsync(older, CancellationToken.None);
        await repository.UpsertSnapshotAsync(newer, CancellationToken.None);

        var latest = await repository.GetLatestSnapshotAsync(CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(newer.SourceRefId, latest.SourceRefId);
        Assert.Equal(newer.IntervalEst, latest.IntervalEst);
        Assert.Equal(newer.TotalMw, latest.TotalMw);
        Assert.Equal(2, latest.Readings.Count);
    }

    [Fact]
    public async Task QueryHistoryAsync_filters_by_range_category_and_limit()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var older = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        var newer = older with
        {
            SourceRefId = "22-Jun-2026 - Interval 11:10 EST",
            IntervalEst = older.IntervalEst.AddMinutes(5)
        };

        await repository.UpsertSnapshotAsync(older, CancellationToken.None);
        await repository.UpsertSnapshotAsync(newer, CancellationToken.None);

        var rows = await repository.QueryHistoryAsync(
            older.IntervalEst.AddMinutes(-1),
            newer.IntervalEst.AddMinutes(1),
            "Coal",
            1,
            CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(newer.SourceRefId, row.SourceRefId);
        Assert.Equal(newer.IntervalEst, row.IntervalEst);
        Assert.Equal("Coal", row.Category);
        Assert.Equal(26869m, row.Mw);
        Assert.Equal("Coal  (26,869 MW)", row.SourceLabel);
    }

    [Fact]
    public async Task GetCategoriesAsync_returns_distinct_categories()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var snapshot = FuelMixParser.Parse(SamplePayloads.FuelMixJson);

        await repository.UpsertSnapshotAsync(snapshot, CancellationToken.None);

        var categories = await repository.GetCategoriesAsync(CancellationToken.None);

        Assert.Equal(["Battery Storage", "Coal"], categories);
    }

    [Fact]
    public async Task GetLatestIngestionRunAsync_returns_most_recent_run()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        var repository = new FuelMixRepository(dataSource);
        var older = FuelMixParser.Parse(SamplePayloads.FuelMixJson);
        var newer = older with
        {
            SourceRefId = "22-Jun-2026 - Interval 11:10 EST",
            IntervalEst = older.IntervalEst.AddMinutes(5)
        };

        await repository.UpsertSnapshotAsync(older, CancellationToken.None);
        await repository.UpsertSnapshotAsync(newer, CancellationToken.None);

        var run = await repository.GetLatestIngestionRunAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(newer.SourceRefId, run.SourceRefId);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task Reader_role_can_select_latest_ingestion_run_status()
    {
        await _postgres.ResetAsync();
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);

        var roles = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "TecFuelMix.Core", "Migrations", "002_roles.sql")));
        await using (var rolesCommand = dataSource.CreateCommand(roles))
        {
            await rolesCommand.ExecuteNonQueryAsync();
        }

        await using var command = dataSource.CreateCommand("""
            select has_table_privilege('fuelmix_reader', 'ingestion_runs', 'select');
            """);

        Assert.True((bool)(await command.ExecuteScalarAsync() ?? false));
    }
}
