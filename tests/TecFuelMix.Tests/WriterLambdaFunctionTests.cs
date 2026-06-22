using Amazon.Lambda.SQSEvents;
using Npgsql;
using TecFuelMix.WriterLambda;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TecFuelMix.Tests;

public sealed class WriterLambdaFunctionTests
{
    private const string ConnectionString =
        "Host=localhost;Port=55432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password";

    [Fact]
    public async Task Handler_persists_valid_records_and_returns_invalid_records_as_batch_failures()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
        var function = new Function(dataSource);
        var context = new TestLambdaContext(TimeSpan.FromSeconds(10));
        var evnt = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage { MessageId = "valid-message", Body = SamplePayloads.FuelMixJson },
                new SQSEvent.SQSMessage { MessageId = "invalid-message", Body = "{}" }
            ]
        };

        var response = await function.Handler(evnt, context);
        var counts = await CountPersistedRows(dataSource);

        var failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("invalid-message", failure.ItemIdentifier);
        Assert.Equal(1L, counts.Snapshots);
        Assert.Equal(2L, counts.Readings);
    }

    [Fact]
    public async Task Handler_returns_valid_record_as_failed_when_invocation_is_already_out_of_time()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await ResetDatabase(dataSource);
        var function = new Function(dataSource);
        var context = new TestLambdaContext(TimeSpan.Zero);
        var evnt = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage { MessageId = "valid-message", Body = SamplePayloads.FuelMixJson }
            ]
        };

        var response = await function.Handler(evnt, context);
        var counts = await CountPersistedRows(dataSource);

        var failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("valid-message", failure.ItemIdentifier);
        Assert.Equal(0L, counts.Snapshots);
        Assert.Equal(0L, counts.Readings);
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
            "Migrations",
            "001_schema.sql");
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

    private static async Task<(long Snapshots, long Readings)> CountPersistedRows(NpgsqlDataSource dataSource)
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
