using DbUp;
using Npgsql;

namespace TecFuelMix.DbMigrator;

public static class MigrationRunner
{
    private const string WriterRole = "fuelmix_writer";
    private const string ReaderRole = "fuelmix_reader";

    public static async Task<int> RunAsync(
        string? connectionString,
        string? writerPassword,
        string? readerPassword,
        TextWriter output,
        TextWriter error)
    {
        var hasWriterPassword = !string.IsNullOrEmpty(writerPassword);
        var hasReaderPassword = !string.IsNullOrEmpty(readerPassword);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync("POSTGRES_ADMIN_CONNECTION_STRING is required.");
            return 2;
        }

        if (hasWriterPassword != hasReaderPassword)
        {
            await error.WriteLineAsync("WRITER_DB_PASSWORD and READ_DB_PASSWORD must be provided together.");
            return 2;
        }

        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(MigrationRunner).Assembly)
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            await error.WriteLineAsync(result.Error.ToString());
            return 1;
        }

        await output.WriteLineAsync("Database migrations applied.");

        try
        {
            await EnsureRoleLoginsAsync(connectionString);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Failed to ensure role login access: {ex.GetType().Name}");
            return 1;
        }

        if (!hasWriterPassword)
        {
            await output.WriteLineAsync("Role passwords were not changed; WRITER_DB_PASSWORD and READ_DB_PASSWORD were not provided.");
            return 0;
        }

        try
        {
            await UpdateRolePasswordsAsync(connectionString, writerPassword!, readerPassword!);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Failed to update role passwords: {ex.GetType().Name}");
            return 1;
        }

        await output.WriteLineAsync("Role passwords updated.");
        return 0;
    }

    private static async Task EnsureRoleLoginsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await SetRoleLoginAsync(connection, WriterRole);
        await SetRoleLoginAsync(connection, ReaderRole);
    }

    private static async Task UpdateRolePasswordsAsync(string connectionString, string writerPassword, string readerPassword)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await SetRolePasswordAsync(connection, WriterRole, writerPassword);
        await SetRolePasswordAsync(connection, ReaderRole, readerPassword);
    }

    private static async Task SetRoleLoginAsync(NpgsqlConnection connection, string roleName)
    {
        ValidateAppRole(roleName);

        await using var command = new NpgsqlCommand($"alter role {roleName} with login;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetRolePasswordAsync(NpgsqlConnection connection, string roleName, string password)
    {
        ValidateAppRole(roleName);

        var alterRoleSql = await BuildAlterRoleSqlAsync(connection, roleName, password);
        await using var command = new NpgsqlCommand(alterRoleSql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> BuildAlterRoleSqlAsync(NpgsqlConnection connection, string roleName, string password)
    {
        await using var command = new NpgsqlCommand(
            $"select format('alter role {roleName} with login password %L', @password);",
            connection);

        command.Parameters.AddWithValue("password", password);

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"Could not build password update SQL for {roleName}."));
    }

    private static void ValidateAppRole(string roleName)
    {
        if (roleName != WriterRole && roleName != ReaderRole)
        {
            throw new ArgumentOutOfRangeException(nameof(roleName), "Only fixed app roles can be updated.");
        }
    }
}
