using TecFuelMix.DbMigrator;

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_ADMIN_CONNECTION_STRING");
var writerPassword = Environment.GetEnvironmentVariable("WRITER_DB_PASSWORD");
var readerPassword = Environment.GetEnvironmentVariable("READ_DB_PASSWORD");
return await MigrationRunner.RunAsync(connectionString, writerPassword, readerPassword, Console.Out, Console.Error);
