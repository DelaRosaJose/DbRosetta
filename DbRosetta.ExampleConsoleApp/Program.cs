internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("DbRosetta Universal Database Translator");
        Console.WriteLine("---------------------------------------");

        // 1. Define the request
        var request = new MigrationRequest
        {
            SourceConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;",
            DestinationDialect = "SQLite",
            DestinationConnectionString = "Host=...;Port=...;Username=...;Password=...;Database=...;SslMode=Require"
        };

        // 2. Create the service
        var migrationService = new MigrationService();

        try
        {
            // 3. Call the service and tell it how to log progress (by writing to the console)
            await migrationService.ExecuteAsync(request, message => Console.WriteLine(message));
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