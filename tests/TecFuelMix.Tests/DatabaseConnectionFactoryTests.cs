using TecFuelMix.Core;

namespace TecFuelMix.Tests;

public sealed class DatabaseConnectionFactoryTests
{
    [Fact]
    public async Task CreateAsync_uses_local_connection_string_fallback()
    {
        using var env = new EnvironmentVariableScope(
            "POSTGRES_CONNECTION_STRING",
            "Host=localhost;Port=5432;Database=fuelmix;Username=fuelmix_app;Password=fuelmix_dev_password");

        await using var dataSource = await DatabaseConnectionFactory.CreateAsync(CancellationToken.None);

        Assert.NotNull(dataSource);
    }
}
