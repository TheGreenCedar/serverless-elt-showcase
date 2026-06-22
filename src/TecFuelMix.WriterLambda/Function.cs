using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using AWS.Lambda.Powertools.Logging;
using AWS.Lambda.Powertools.Metrics;
using Npgsql;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.WriterLambda;

public sealed class Function
{
    private const string MetricsNamespace = "TecFuelMix";
    private const string ServiceName = "WriterLambda";
    private static readonly TimeSpan TimeoutSafetyBuffer = TimeSpan.FromSeconds(1);
    private readonly Func<CancellationToken, Task<NpgsqlDataSource>> _dataSourceFactory;
    private readonly SemaphoreSlim _dataSourceLock = new(1, 1);
    private NpgsqlDataSource? _dataSource;

    public Function()
    {
        _dataSourceFactory = DatabaseConnectionFactory.CreateAsync;
    }

    public Function(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _dataSourceFactory = _ => Task.FromResult(dataSource);
    }

    public async Task<SQSBatchResponse> Handler(SQSEvent evnt, ILambdaContext context)
    {
        using var timeout = CreateInvocationTimeout(context);
        var cancellationToken = timeout.Token;
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        var repository = new FuelMixRepository(await GetDataSourceAsync(cancellationToken));

        foreach (var record in evnt.Records)
        {
            try
            {
                var snapshot = FuelMixParser.Parse(record.Body);
                await repository.UpsertSnapshotAsync(snapshot, cancellationToken);
                Metrics.PushSingleMetric("FuelMixWriteSucceeded", 1, MetricUnit.Count, MetricsNamespace, ServiceName);
                Logger.LogInformation("Persisted MISO FuelMix snapshot {SourceRefId}.", snapshot.SourceRefId);
            }
            catch (Exception ex)
            {
                Metrics.PushSingleMetric("FuelMixWriteFailed", 1, MetricUnit.Count, MetricsNamespace, ServiceName);
                Logger.LogError(
                    "Failed to persist SQS message {MessageId} with {ErrorType}.",
                    record.MessageId,
                    ex.GetType().Name);
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        if (failures.Count > 0)
        {
            Metrics.PushSingleMetric("FuelMixPartialBatchFailures", failures.Count, MetricUnit.Count, MetricsNamespace, ServiceName);
        }

        return new SQSBatchResponse(failures);
    }

    private async Task<NpgsqlDataSource> GetDataSourceAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is { } dataSource)
        {
            return dataSource;
        }

        await _dataSourceLock.WaitAsync(cancellationToken);
        try
        {
            if (_dataSource is { } cachedDataSource)
            {
                return cachedDataSource;
            }

            dataSource = await _dataSourceFactory(cancellationToken);
            _dataSource = dataSource;
            return dataSource;
        }
        finally
        {
            _dataSourceLock.Release();
        }
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
