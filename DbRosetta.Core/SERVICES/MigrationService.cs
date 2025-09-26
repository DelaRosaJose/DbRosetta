using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data.Common;

public class MigrationService
{
    public async Task ExecuteAsync(MigrationRequest request, Action<string> logProgress)
    {
        // --- ADD THIS VALIDATION BLOCK ---
        if (request == null)
            throw new ArgumentNullException(nameof(request), "Migration request cannot be null.");
        if (string.IsNullOrWhiteSpace(request.SourceDialect))
            throw new ArgumentException("Source dialect must be specified in the migration request.", nameof(request.SourceDialect));
        if (string.IsNullOrWhiteSpace(request.DestinationDialect))
            throw new ArgumentException("Destination dialect must be specified in the migration request.", nameof(request.DestinationDialect));
        if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
            throw new ArgumentException("Source connection string must be provided.", nameof(request.SourceConnectionString));
        if (string.IsNullOrWhiteSpace(request.DestinationConnectionString))
            throw new ArgumentException("Destination connection string must be provided.", nameof(request.DestinationConnectionString));
        // --- END VALIDATION BLOCK ---


        IDatabaseWriter schemaWriter;
        DbConnection destinationConnection;

        // --- THIS IS THE FIX ---
        // By restructuring to a single if/else if/else chain, we guarantee
        // that the variables are assigned in every valid path.
        if (request.DestinationDialect == "SQLite")
        {
            var outputSqliteFile = "MyTranslatedDb.sqlite"; // Or get from request
            if (File.Exists(outputSqliteFile)) File.Delete(outputSqliteFile);
            destinationConnection = new SqliteConnection($"Data Source={outputSqliteFile}");
            schemaWriter = new SQLiteWriter();
        }
        else if (request.DestinationDialect == "PostgreSql")
        {
            destinationConnection = new NpgsqlConnection(request.DestinationConnectionString);
            schemaWriter = new PostgreSqlWriter();
        }
        else
        {
            // The only other path now is to throw an exception, so the compiler knows
            // the code below is unreachable unless the variables are assigned.
            throw new NotSupportedException($"Unsupported destination dialect: {request.DestinationDialect}");
        }

        var typeService = new TypeMappingService(GetDialects());
        var schemaReader = new SqlServerSchemaReader();
        var dataMigrator = new DataMigrator();

        try
        {
            await using var sqlConnection = new SqlConnection(request.SourceConnectionString);
            await sqlConnection.OpenAsync();
            await destinationConnection.OpenAsync(); // Now guaranteed to be assigned

            logProgress($"Successfully connected to source ({request.SourceDialect}) and destination ({request.DestinationDialect}).");

            // --- [PHASE 1] READ AND CREATE BASE SCHEMA ---
            logProgress("\n[Phase 1/3] Reading and creating base schema...");
            List<TableSchema> tables = await schemaReader.GetTablesAsync(sqlConnection);
            List<ViewSchema> views = await schemaReader.GetViewsAsync(sqlConnection);
            logProgress($"Found {tables.Count} tables and {views.Count} views to translate.");

            await schemaWriter.WriteSchemaAsync(destinationConnection, tables, typeService, request.SourceDialect);
            await schemaWriter.WriteViewsAsync(destinationConnection, views);
            logProgress("[Phase 1/3] Base schema creation complete.");

            // --- [PHASE 2] MIGRATE DATA ---
            logProgress("\n[Phase 2/3] Migrating data...");
            await dataMigrator.MigrateDataAsync(sqlConnection, destinationConnection, tables,
                (tableName, rows) =>
                {
                    logProgress($"\r  -> Migrating {tableName}: {rows} rows transferred...");
                });
            logProgress("\n[Phase 2/3] Data migration complete.");

            // --- [PHASE 3] APPLY INDEXES AND CONSTRAINTS ---
            if (schemaWriter is PostgreSqlWriter pgWriter)
            {
                await pgWriter.WriteConstraintsAndIndexesAsync(destinationConnection);
                logProgress("[Phase 3/3] Indexes and constraints applied.");
            }

            logProgress("\n---------------------------------------");
            logProgress("✅ Migration completed successfully!");
        }
        catch (Exception ex)
        {
            logProgress($"❌ An error occurred during migration: {ex}");
            throw;
        }
        finally
        {
            // Now guaranteed to be assigned
            if (destinationConnection != null && destinationConnection.State != System.Data.ConnectionState.Closed)
            {
                await destinationConnection.CloseAsync();
            }
        }
    }

    private List<IDatabaseDialect> GetDialects()
    {
        return new List<IDatabaseDialect>
        {
            new SqlServerDialect(),
            new PostgreSqlDialect(),
            new SQLiteDialect()
        };
    }
}