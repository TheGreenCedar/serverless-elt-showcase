using Npgsql;

namespace TecFuelMix.Tests;

internal static class TestDatabase
{
    public const string LocalConnectionString =
        "Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password";

    public static string MigrationPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "TecFuelMix.Core", "Migrations", fileName));
    }

    public static async Task ResetAsync(NpgsqlDataSource dataSource)
    {
        var schema = await File.ReadAllTextAsync(MigrationPath("001_schema.sql"));
        await using (var schemaCommand = dataSource.CreateCommand(schema))
        {
            await schemaCommand.ExecuteNonQueryAsync();
        }

        await using var cleanup = dataSource.CreateCommand("""
            truncate table ingestion_runs, fuel_mix_readings, fuel_mix_snapshots restart identity cascade;
            """);
        await cleanup.ExecuteNonQueryAsync();
    }

    public static async Task<NpgsqlDataSource> CreateResetDataSourceAsync()
    {
        var dataSource = NpgsqlDataSource.Create(LocalConnectionString);
        try
        {
            await ResetAsync(dataSource);
            return dataSource;
        }
        catch
        {
            await dataSource.DisposeAsync();
            throw;
        }
    }

    public static async Task<(long Snapshots, long Readings)> CountRowsAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("""
            select
                (select count(*) from fuel_mix_snapshots) as snapshot_count,
                (select count(*) from fuel_mix_readings) as reading_count
            """);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}
