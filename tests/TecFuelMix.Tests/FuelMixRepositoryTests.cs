using Npgsql;
using TecFuelMix.Core;

namespace TecFuelMix.Tests;

public sealed class FuelMixRepositoryTests
{
    private const string ConnectionString =
        "Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password";

    [Fact]
    public async Task UpsertSnapshotAsync_is_idempotent_by_source_ref_and_category()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
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
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
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

    private static async Task ResetDatabase(NpgsqlDataSource dataSource)
    {
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "TecFuelMix.Core",
            "Schema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath);
        await using (var schemaCommand = dataSource.CreateCommand(schema))
        {
            await schemaCommand.ExecuteNonQueryAsync();
        }

        await using var cleanup = dataSource.CreateCommand("""
            truncate table ingestion_runs, fuel_mix_readings, fuel_mix_snapshots restart identity cascade;
            """);
        await cleanup.ExecuteNonQueryAsync();
    }
}
