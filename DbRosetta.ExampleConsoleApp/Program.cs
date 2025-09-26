using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data.Common;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("DbRosetta Universal Database Translator");
        Console.WriteLine("---------------------------------------");

        // --- Configuration ---
        string destinationDialect = "PostgreSql"; // Change to "SQLite" to run the other path
        var sqlServerConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;";
        var pgConnectionString = "YourPostgreconnection";
        var outputSqliteFile = "MyTranslatedDb.sqlite";

        // --- Service Initialization ---
        IDatabaseWriter schemaWriter;
        DbConnection destinationConnection;

        if (destinationDialect == "SQLite")
        {
            if (File.Exists(outputSqliteFile)) File.Delete(outputSqliteFile);
            destinationConnection = new SqliteConnection($"Data Source={outputSqliteFile}");
            schemaWriter = new SQLiteWriter();
        }
        else if (destinationDialect == "PostgreSql")
        {
            destinationConnection = new NpgsqlConnection(pgConnectionString);
            schemaWriter = new PostgreSqlWriter();
        }
        else
        {
            Console.WriteLine("Unsupported destination dialect.");
            return;
        }

        var typeService = new TypeMappingService(GetDialects());
        var schemaReader = new SqlServerSchemaReader();
        var dataMigrator = new DataMigrator();

        try
        {
            await using var sqlConnection = new SqlConnection(sqlServerConnectionString);
            await sqlConnection.OpenAsync();
            await destinationConnection.OpenAsync();

            Console.WriteLine($"Successfully connected to source (SQL Server) and destination ({destinationDialect}).");

            // --- [PHASE 1] READ AND CREATE BASE SCHEMA ---
            Console.WriteLine("\n[Phase 1/3] Reading and creating base schema...");
            List<TableSchema> tables = await schemaReader.GetTablesAsync(sqlConnection);
            List<ViewSchema> views = await schemaReader.GetViewsAsync(sqlConnection);
            Console.WriteLine($"Found {tables.Count} tables and {views.Count} views to translate.");

            // Writes tables (without indexes/constraints for PostgreSQL) and views.
            await schemaWriter.WriteSchemaAsync(destinationConnection, tables, typeService, "SqlServer");
            await schemaWriter.WriteViewsAsync(destinationConnection, views);
            Console.WriteLine("[Phase 1/3] Base schema creation complete.");

            // --- [PHASE 2] MIGRATE DATA ---
            Console.WriteLine("\n[Phase 2/3] Migrating data...");
            await dataMigrator.MigrateDataAsync(sqlConnection, destinationConnection, tables,
                (tableName, rows) =>
                {
                    Console.Write($"\r  -> Migrating {tableName}: {rows} rows transferred...");
                });
            Console.WriteLine("\n[Phase 2/3] Data migration complete.");

            // --- [PHASE 3] APPLY INDEXES AND CONSTRAINTS (FOR SUPPORTED WRITERS) ---
            // This is the crucial new step that applies constraints after data is loaded.
            if (schemaWriter is PostgreSqlWriter pgWriter)
            {
                // The writer instance already holds the schema information from Phase 1.
                await pgWriter.WriteConstraintsAndIndexesAsync(destinationConnection);
                Console.WriteLine("[Phase 3/3] Indexes and constraints applied.");
            }

            Console.WriteLine("\n---------------------------------------");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Migration completed successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("❌ An error occurred during migration:");
            Console.WriteLine(ex.ToString());
            Console.ResetColor();
        }
        finally
        {
            await destinationConnection.CloseAsync();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }

    static List<IDatabaseDialect> GetDialects()
    {
        return new List<IDatabaseDialect>
            {
                new SqlServerDialect(),
                new PostgreSqlDialect(),
                // new MySqlDialect(), // Assuming this exists
                new SQLiteDialect()
            };
    }
}