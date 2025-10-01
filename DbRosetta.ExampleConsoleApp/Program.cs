using DbRosetta.Core.Services;
// Located at: DbRosetta.ExampleConsoleApp/Program.cs

using DbRosetta.Core; // <-- ADD THIS using statement
using DbRosetta.Core.Services; // <-- ADD THIS if you used the sub-namespace

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
            DestinationConnectionString = "Data.sqlite"
        };


        var migrationService = new MigrationService(new ConsoleProgressHandler()); // <-- See below

        try
        {
            // 3. Call the service and tell it how to log progress (by writing to the console)
            await migrationService.ExecuteAsync(request);
        }
        catch (Exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nMigration failed. Check logs for details.");
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}