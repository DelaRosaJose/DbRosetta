using DbRosetta.Core.Services;
// Located at: DbRosetta.ExampleConsoleApp/Program.cs

using DbRosetta.Core; // <-- ADD THIS using statement
using DbRosetta.Core.Services; // <-- ADD THIS if you used the sub-namespace
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("DbRosetta Universal Database Translator");
        Console.WriteLine("---------------------------------------");

        // 1. Define the request
        var request = new MigrationRequest
        {
            SourceDialect = DatabaseEngine.SqlServer, // Add this line
            SourceConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;",
            DestinationDialect = DatabaseEngine.SQLite,
            DestinationConnectionString = @"E:\DbRosetta\Output\AdventureWorks.sqlite"
        };


        // The service and handler are created here.
        var progressHandler = new ConsoleProgressHandler();
        var factory = new DatabaseProviderFactory();
        var logger = new NullLogger<DataMigratorService>();
        var dataMigrator = new DataMigratorService(factory, logger);
        var migrationService = new MigrationService(progressHandler, factory, dataMigrator);

        try
        {
            // The "await" keyword ensures the application waits here until the
            // entire migration is finished before moving to the finally block.
            await migrationService.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            // The service will log its own detailed error via the handler.
            // This is a final catch-all for any unexpected failures.
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"\nAn unhandled exception occurred in the application: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}