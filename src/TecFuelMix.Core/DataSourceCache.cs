using Npgsql;

namespace TecFuelMix.Core;

public sealed class DataSourceCache
{
    private readonly Func<CancellationToken, Task<NpgsqlDataSource>> _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private NpgsqlDataSource? _dataSource;

    public DataSourceCache(Func<CancellationToken, Task<NpgsqlDataSource>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public DataSourceCache(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _factory = _ => Task.FromResult(dataSource);
        _dataSource = dataSource;
    }

    public async Task<NpgsqlDataSource> GetAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is { } dataSource)
        {
            return dataSource;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_dataSource is { } cachedDataSource)
            {
                return cachedDataSource;
            }

            dataSource = await _factory(cancellationToken);
            _dataSource = dataSource;
            return dataSource;
        }
        finally
        {
            _lock.Release();
        }
    }
}
