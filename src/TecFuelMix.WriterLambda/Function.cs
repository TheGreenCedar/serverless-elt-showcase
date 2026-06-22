using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.WriterLambda;

public sealed class Function
{
    private static readonly TimeSpan TimeoutSafetyBuffer = TimeSpan.FromSeconds(1);
    private readonly NpgsqlDataSource _dataSource;

    public Function()
        : this(NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? ""))
    {
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<SQSBatchResponse> Handler(SQSEvent evnt, ILambdaContext context)
    {
        using var timeout = CreateInvocationTimeout(context);
        var cancellationToken = timeout.Token;
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        var repository = new FuelMixRepository(_dataSource);

        foreach (var record in evnt.Records)
        {
            try
            {
                var snapshot = FuelMixParser.Parse(record.Body);
                await repository.UpsertSnapshotAsync(snapshot, cancellationToken);
                context.Logger.LogInformation($"Persisted MISO FuelMix snapshot {snapshot.SourceRefId}.");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Failed to persist SQS message {record.MessageId}: {ex}");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        return new SQSBatchResponse(failures);
    }

    private static CancellationTokenSource CreateInvocationTimeout(ILambdaContext context)
    {
        var timeout = new CancellationTokenSource();
        var remaining = context.RemainingTime;

        if (remaining <= TimeoutSafetyBuffer)
        {
            timeout.Cancel();
            return timeout;
        }

        timeout.CancelAfter(remaining - TimeoutSafetyBuffer);
        return timeout;
    }
}
