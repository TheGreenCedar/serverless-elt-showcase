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
}
