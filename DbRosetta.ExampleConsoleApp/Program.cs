using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;


internal class Program
{
    static async Task Main(string[] args)
    {
        // ==========================================================
        //  FIX: Initialize the native SQLite library provider
        // ==========================================================
        SQLitePCL.Batteries.Init();

        Console.WriteLine("DbRosetta Universal Database Translator");
        Console.WriteLine("---------------------------------------");

        // --- CONFIGURATION ---
        // !!! IMPORTANT: Replace this with your actual SQL Server connection string !!!
        var sqlServerConnectionString = "Server=MSI\\SQLEXPRESS;Database=Cartografia;Trusted_Connection=True;TrustServerCertificate=True;";

        var outputSqliteFile = "MyTranslatedDb.sqlite";

        // 1. Register all services and dialects
        var dialects = new List<IDatabaseDialect>
        {
            new SqlServerDialect(),
            new PostgreSqlDialect(),
            new MySqlDialect(),
            new SQLiteDialect()
        };

        var typeService = new TypeMappingService(dialects);
        var schemaReader = new SqlServerSchemaReader();
        var schemaWriter = new SQLiteWriter();

        // --- EXECUTION ---
        try
        {
            // Ensure the old SQLite file is deleted for a clean run
            if (File.Exists(outputSqliteFile))
            {
                File.Delete(outputSqliteFile);
                Console.WriteLine($"Deleted existing file: {outputSqliteFile}");
            }

            // 2. Open connections to both databases
            await using var sqlConnection = new SqlConnection(sqlServerConnectionString);
            await using var sqliteConnection = new SqliteConnection($"Data Source={outputSqliteFile}");

            await sqlConnection.OpenAsync();
            await sqliteConnection.OpenAsync();
            Console.WriteLine("Successfully connected to source (SQL Server) and destination (SQLite).");

            // 3. Read the schema from the source database
            Console.WriteLine("Reading schema from SQL Server...");
            var sourceTables = await schemaReader.GetTablesAsync(sqlConnection);
            Console.WriteLine($"Found {sourceTables.Count} tables to translate.");

            // 4. Write the translated schema to the destination database
            Console.WriteLine("Writing translated schema to SQLite...");
            await schemaWriter.WriteSchemaAsync(sqliteConnection, sourceTables, typeService, "SqlServer");

            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("✅ Schema migration completed successfully!");
            Console.WriteLine($"A new database file has been created at: {Path.GetFullPath(outputSqliteFile)}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("❌ An error occurred during migration:");
            Console.WriteLine(ex.Message);
            // Also print stack trace for better debugging
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }
}