using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;


internal class Program
{
    static async Task Main(string[] args)
    {
        SQLitePCL.Batteries.Init();
        Console.WriteLine("DbRosetta Universal Database Translator");
        Console.WriteLine("---------------------------------------");

        var sqlServerConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;";
        var outputSqliteFile = "MyTranslatedDb.sqlite";

        var typeService = new TypeMappingService(GetDialects());
        var schemaReader = new SqlServerSchemaReader();
        var schemaWriter = new SQLiteWriter();
        var dataMigrator = new DataMigrator(); // Instantiate the new service

        try
        {
            if (File.Exists(outputSqliteFile))
            {
                File.Delete(outputSqliteFile);
                Console.WriteLine($"Deleted existing file: {outputSqliteFile}");
            }

            await using var sqlConnection = new SqlConnection(sqlServerConnectionString);
            await using var sqliteConnection = new SqliteConnection($"Data Source={outputSqliteFile}");
            await sqlConnection.OpenAsync();
            await sqliteConnection.OpenAsync();
            Console.WriteLine("Successfully connected to source (SQL Server) and destination (SQLite).");

            // --- STEP 1: READ SCHEMA ---
            Console.WriteLine("\n[Phase 1/2] Reading schema from SQL Server...");
            var sourceTables = await schemaReader.GetTablesAsync(sqlConnection);
            Console.WriteLine($"Found {sourceTables.Count} tables to translate.");

            // --- STEP 2: WRITE SCHEMA ---
            Console.WriteLine("[Phase 1/2] Writing translated schema to SQLite...");
            await schemaWriter.WriteSchemaAsync(sqliteConnection, sourceTables, typeService, "SqlServer");
            Console.WriteLine("[Phase 1/2] Schema creation complete.");

            // --- STEP 3: MIGRATE DATA ---
            Console.WriteLine("\n[Phase 2/2] Migrating data from source to destination...");
            await dataMigrator.MigrateDataAsync(sqlConnection, sqliteConnection, sourceTables,
                (tableName, rows) => {
                    // This is our progress reporting action
                    Console.Write($"\r  -> Migrating {tableName}: {rows} rows transferred...");
                });
            Console.WriteLine("\n[Phase 2/2] Data migration complete.");

            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("✅ Migration completed successfully!");
            Console.WriteLine($"A new database file has been created at: {Path.GetFullPath(outputSqliteFile)}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("❌ An error occurred during migration:");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }

    static List<IDatabaseDialect> GetDialects()
    {
        return new List<IDatabaseDialect>
        {
            new SqlServerDialect(),
            new PostgreSqlDialect(),
            new MySqlDialect(),
            new SQLiteDialect()
        };
    }
}