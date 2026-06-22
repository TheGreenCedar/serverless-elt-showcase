using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace TecFuelMix.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("fuelmix")
        .WithUsername("fuelmix_app")
        .WithPassword("fuelmix_dev_password")
        .Build();

    private Respawner? _respawner;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var schema = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "TecFuelMix.Core", "Migrations", "001_schema.sql")));

        await using (var command = new NpgsqlCommand(schema, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }
}
