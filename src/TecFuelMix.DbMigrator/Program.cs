using DbUp;
using Npgsql;

const string WriterRole = "fuelmix_writer";
const string ReaderRole = "fuelmix_reader";

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_ADMIN_CONNECTION_STRING");
var writerPassword = Environment.GetEnvironmentVariable("WRITER_DB_PASSWORD");
var readerPassword = Environment.GetEnvironmentVariable("READ_DB_PASSWORD");
var hasWriterPassword = !string.IsNullOrEmpty(writerPassword);
var hasReaderPassword = !string.IsNullOrEmpty(readerPassword);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POSTGRES_ADMIN_CONNECTION_STRING is required.");
    return 2;
}

if (hasWriterPassword != hasReaderPassword)
{
    Console.Error.WriteLine("WRITER_DB_PASSWORD and READ_DB_PASSWORD must be provided together.");
    return 2;
}

var result = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
    .LogToConsole()
    .Build()
    .PerformUpgrade();

if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("Database migrations applied.");

try
{
    await EnsureRoleLoginsAsync(connectionString);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to ensure role login access: {ex.GetType().Name}");
    return 1;
}

if (!hasWriterPassword)
{
    Console.WriteLine("Role passwords were not changed; WRITER_DB_PASSWORD and READ_DB_PASSWORD were not provided.");
    return 0;
}

try
{
    await UpdateRolePasswordsAsync(connectionString, writerPassword!, readerPassword!);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to update role passwords: {ex.GetType().Name}");
    return 1;
}

Console.WriteLine("Role passwords updated.");
return 0;

static async Task EnsureRoleLoginsAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await SetRoleLoginAsync(connection, WriterRole);
    await SetRoleLoginAsync(connection, ReaderRole);
}

static async Task UpdateRolePasswordsAsync(string connectionString, string writerPassword, string readerPassword)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await SetRolePasswordAsync(connection, WriterRole, writerPassword);
    await SetRolePasswordAsync(connection, ReaderRole, readerPassword);
}

static async Task SetRoleLoginAsync(NpgsqlConnection connection, string roleName)
{
    ValidateAppRole(roleName);

    await using var command = new NpgsqlCommand($"alter role {roleName} with login;", connection);
    await command.ExecuteNonQueryAsync();
}

static async Task SetRolePasswordAsync(NpgsqlConnection connection, string roleName, string password)
{
    ValidateAppRole(roleName);

    var alterRoleSql = await BuildAlterRoleSqlAsync(connection, roleName, password);
    await using var command = new NpgsqlCommand(alterRoleSql, connection);

    await command.ExecuteNonQueryAsync();
}

static async Task<string> BuildAlterRoleSqlAsync(NpgsqlConnection connection, string roleName, string password)
{
    await using var command = new NpgsqlCommand(
        $"select format('alter role {roleName} with login password %L', @password);",
        connection);

    command.Parameters.AddWithValue("password", password);

    return (string)(await command.ExecuteScalarAsync()
        ?? throw new InvalidOperationException($"Could not build password update SQL for {roleName}."));
}

static void ValidateAppRole(string roleName)
{
    if (roleName != WriterRole && roleName != ReaderRole)
    {
        throw new ArgumentOutOfRangeException(nameof(roleName), "Only fixed app roles can be updated.");
    }
}
