using DbUp;

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_ADMIN_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POSTGRES_ADMIN_CONNECTION_STRING is required.");
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
return 0;
