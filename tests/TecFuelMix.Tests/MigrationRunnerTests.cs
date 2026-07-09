using Npgsql;
using TecFuelMix.DbMigrator;

namespace TecFuelMix.Tests;

public sealed class MigrationRunnerTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public MigrationRunnerTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task RunAsync_requires_admin_connection_string()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await MigrationRunner.RunAsync(null, null, null, output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("POSTGRES_ADMIN_CONNECTION_STRING", error.ToString());
    }

    [Fact]
    public async Task RunAsync_requires_runtime_passwords_together()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await MigrationRunner.RunAsync("Host=localhost", "writer", null, output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("provided together", error.ToString());
    }

    [Fact]
    public async Task RunAsync_applies_migrations_updates_role_passwords_and_reruns_cleanly()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var writerPassword = "writer-test-password-1!";
        var readerPassword = "reader-test-password-1!";

        var firstExitCode = await MigrationRunner.RunAsync(
            _postgres.ConnectionString,
            writerPassword,
            readerPassword,
            output,
            error);
        var secondExitCode = await MigrationRunner.RunAsync(
            _postgres.ConnectionString,
            writerPassword,
            readerPassword,
            output,
            error);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Contains("Database migrations applied.", output.ToString());
        Assert.Empty(error.ToString());

        await using (var writerConnection = new NpgsqlConnection(RoleConnectionString("fuelmix_writer", writerPassword)))
        {
            await writerConnection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "insert into ingestion_runs(status) values ('succeeded');",
                writerConnection);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await using (var readerConnection = new NpgsqlConnection(RoleConnectionString("fuelmix_reader", readerPassword)))
        {
            await readerConnection.OpenAsync();
            await using var select = new NpgsqlCommand(
                "select count(*) from ingestion_runs;",
                readerConnection);
            Assert.Equal(1L, (long)(await select.ExecuteScalarAsync() ?? 0L));
        }
    }

    private string RoleConnectionString(string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString)
        {
            Username = username,
            Password = password
        };

        return builder.ConnectionString;
    }
}
