using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace DbRosetta.ExampleConsoleApp
{
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
            var dataMigrator = new DataMigrator();

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

                // --- STEP 1: READ SCHEMA (Tables and Views) ---
                Console.WriteLine("\n[Phase 1/2] Reading schema from SQL Server...");
                var sourceTables = await schemaReader.GetTablesAsync(sqlConnection);
                var sourceViews = await schemaReader.GetViewsAsync(sqlConnection); // Read the views
                Console.WriteLine($"Found {sourceTables.Count} tables and {sourceViews.Count} views to translate.");

                // --- STEP 2: WRITE SCHEMA (Tables and Views) ---
                Console.WriteLine("[Phase 1/2] Writing translated schema to SQLite...");
                await schemaWriter.WriteSchemaAsync(sqliteConnection, sourceTables, typeService, "SqlServer");
                await schemaWriter.WriteViewsAsync(sqliteConnection, sourceViews); // Write the views
                Console.WriteLine("[Phase 1/2] Schema creation complete.");

                // --- STEP 3: MIGRATE DATA ---
                Console.WriteLine("\n[Phase 2/2] Migrating data from source to destination...");
                // Note: The data migrator only works on tables, so we pass sourceTables.
                await dataMigrator.MigrateDataAsync(sqlConnection, sqliteConnection, sourceTables,
                    (tableName, rows) => {
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
                // Use ex.ToString() to get the full exception details, including inner exceptions
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
            }
            finally
            {
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
}