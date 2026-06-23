using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;
using TecFuelMix.Core;
using TecFuelMix.DbMigrator;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.MigratorLambda;

public sealed class Function
{
    private static readonly JsonSerializerOptions SecretJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAmazonSecretsManager _secretsManager;

    public Function()
        : this(new AmazonSecretsManagerClient())
    {
    }

    public Function(IAmazonSecretsManager secretsManager)
    {
        _secretsManager = secretsManager;
    }

    public async Task<object> Handler(object request, ILambdaContext context)
    {
        var host = Required("POSTGRES_HOST");
        var database = Required("POSTGRES_DATABASE");
        var adminSecret = await GetSecretAsync(Required("POSTGRES_ADMIN_SECRET_ARN"));
        var writerSecret = await GetSecretAsync(Required("WRITER_DB_SECRET_ARN"));
        var readSecret = await GetSecretAsync(Required("READ_DB_SECRET_ARN"));

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = 5432,
            Database = database,
            Username = adminSecret.Username,
            Password = adminSecret.Password,
            SslMode = SslMode.Require
        }.ConnectionString;

        var exitCode = await MigrationRunner.RunAsync(
            connectionString,
            writerSecret.Password,
            readSecret.Password,
            Console.Out,
            Console.Error);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Database migration failed with exit code {exitCode}.");
        }

        return new { status = "migrated" };
    }

    private async Task<DatabaseSecret> GetSecretAsync(string secretArn)
    {
        var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn
        }, CancellationToken.None);

        if (string.IsNullOrWhiteSpace(response.SecretString))
        {
            throw new InvalidOperationException("Database secret must contain username and password.");
        }

        var secret = JsonSerializer.Deserialize<DatabaseSecret>(response.SecretString, SecretJsonOptions);
        if (secret is null || string.IsNullOrWhiteSpace(secret.Username) || string.IsNullOrWhiteSpace(secret.Password))
        {
            throw new InvalidOperationException("Database secret must contain username and password.");
        }

        return secret;
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }
}
