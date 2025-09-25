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

        string destinationDialect = "PostgreSql"; // Change to "SQLite" to run the other path

        IDatabaseWriter schemaWriter;
        DbConnection destinationConnection;

        var sqlServerConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;";

        if (destinationDialect == "SQLite")
        {
            var outputSqliteFile = "MyTranslatedDb.sqlite";
            if (File.Exists(outputSqliteFile)) File.Delete(outputSqliteFile);
            destinationConnection = new SqliteConnection($"Data Source={outputSqliteFile}");
            schemaWriter = new SQLiteWriter();
        }
        else if (destinationDialect == "PostgreSql")
        {
            // --- THIS IS THE FIX ---
            // The connection string is now in the correct key-value pair format.
            var pgConnectionString = "YourConnectionString";

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
            // Open connections inside the 'using' block
            await sqlConnection.OpenAsync();
            await destinationConnection.OpenAsync();

            Console.WriteLine($"Successfully connected to source (SQL Server) and destination ({destinationDialect}).");

            Console.WriteLine("\n[Phase 1/2] Reading schema from SQL Server...");
            List<TableSchema> tables = await schemaReader.GetTablesAsync(sqlConnection);
            List<ViewSchema> views = await schemaReader.GetViewsAsync(sqlConnection);
            Console.WriteLine($"Found {tables.Count} tables and {views.Count} views to translate.");

            Console.WriteLine("[Phase 1/2] Writing translated schema...");
            await schemaWriter.WriteSchemaAsync(destinationConnection, tables, typeService, "SqlServer");
            await schemaWriter.WriteViewsAsync(destinationConnection, views);
            Console.WriteLine("[Phase 1/2] Schema creation complete.");

            Console.WriteLine("\n[Phase 2/2] Migrating data...");
            await dataMigrator.MigrateDataAsync(sqlConnection, destinationConnection, tables,
                (tableName, rows) =>
                {
                    Console.Write($"\r  -> Migrating {tableName}: {rows} rows transferred...");
                });
            Console.WriteLine("\n[Phase 2/2] Data migration complete.");

            Console.WriteLine("\n---------------------------------------");
            Console.WriteLine("✅ Migration completed successfully!");
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
                new MySqlDialect(),
                new SQLiteDialect()
            };
    }
}
