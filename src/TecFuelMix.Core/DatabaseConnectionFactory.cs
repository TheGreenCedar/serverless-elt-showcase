using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

namespace TecFuelMix.Core;

public static class DatabaseConnectionFactory
{
    public static async Task<NpgsqlDataSource> CreateAsync(CancellationToken cancellationToken)
    {
        var direct = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return NpgsqlDataSource.Create(direct);
        }

        var host = Required("POSTGRES_HOST");
        var database = Required("POSTGRES_DATABASE");
        var secretArn = Required("POSTGRES_SECRET_ARN");

        using var secrets = new AmazonSecretsManagerClient();
        var response = await secrets.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.SecretString))
        {
            throw new InvalidOperationException("Database secret must contain username and password.");
        }

        var secret = JsonSerializer.Deserialize<DatabaseSecret>(
            response.SecretString,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (secret is null || string.IsNullOrWhiteSpace(secret.Username) || string.IsNullOrWhiteSpace(secret.Password))
        {
            throw new InvalidOperationException("Database secret must contain username and password.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = 5432,
            Database = database,
            Username = secret.Username,
            Password = secret.Password,
            SslMode = SslMode.Require
        };

        return NpgsqlDataSource.Create(builder.ConnectionString);
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
