using Amazon.Lambda.SQSEvents;
using TecFuelMix.WriterLambda;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TecFuelMix.Tests;

public sealed class WriterLambdaFunctionTests
{
    [Fact]
    public async Task Handler_persists_valid_records_and_returns_invalid_records_as_batch_failures()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
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
        var counts = await TestDatabase.CountRowsAsync(dataSource);

        var failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("invalid-message", failure.ItemIdentifier);
        Assert.Equal(1L, counts.Snapshots);
        Assert.Equal(2L, counts.Readings);
    }

    [Fact]
    public async Task Handler_returns_valid_record_as_failed_when_invocation_is_already_out_of_time()
    {
        await using var dataSource = await TestDatabase.CreateResetDataSourceAsync();
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
        var counts = await TestDatabase.CountRowsAsync(dataSource);

        var failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("valid-message", failure.ItemIdentifier);
        Assert.Equal(0L, counts.Snapshots);
        Assert.Equal(0L, counts.Readings);
    }

}
