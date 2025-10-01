// Located at: DbRosetta.Core/SERVICES/MigrationService.cs

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace DbRosetta.Core.Services // Using a sub-namespace is good practice
{
    /// <summary>
    /// The core engine for performing a database migration.
    /// This service is completely decoupled from any UI or API layer.
    /// It depends on the IMigrationProgressHandler interface to report its status.
    /// </summary>
    public class MigrationService
    {
        private readonly IMigrationProgressHandler _progressHandler;

        /// <summary>
        /// The constructor requires a concrete implementation of the progress handler.
        /// </summary>
        /// <param name="progressHandler">The handler that will receive progress updates.</param>
        public MigrationService(IMigrationProgressHandler progressHandler)
        {
            _progressHandler = progressHandler;
        }

        /// <summary>
        /// Executes the entire three-phase migration process.
        /// </summary>
        public async Task ExecuteAsync(MigrationRequest request)
        {
            // --- 1. Input Validation (Guard Clauses) ---
            if (request == null)
                throw new ArgumentNullException(nameof(request), "Migration request cannot be null.");
            if (string.IsNullOrWhiteSpace(request.SourceDialect.ToString()))
                throw new ArgumentException("Source dialect must be specified.", nameof(request.SourceDialect));
            if (string.IsNullOrWhiteSpace(request.DestinationDialect.ToString()))
                throw new ArgumentException("Destination dialect must be specified.", nameof(request.DestinationDialect));
            if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
                throw new ArgumentException("Source connection string must be provided.", nameof(request.SourceConnectionString));
            if (string.IsNullOrWhiteSpace(request.DestinationConnectionString))
                throw new ArgumentException("Destination connection string must be provided.", nameof(request.DestinationConnectionString));

            // --- 2. Setup Connections and Services ---
            IDatabaseWriter schemaWriter;
            DbConnection destinationConnection;

            switch (request.DestinationDialect)
            {
                case DatabaseEngine.SQLite:
                    // --- THIS IS THE FIX ---
                    // The connection string from the UI *is* the file path.
                    var outputSqliteFile = request.DestinationConnectionString;

                    // The connection string for SQLite needs to be in the format "Data Source=C:\path\to\file.db"
                    var sqliteConnectionString = $"Data Source={outputSqliteFile}";

                    // Delete the file if it already exists for a clean migration
                    if (File.Exists(outputSqliteFile))
                    {
                        File.Delete(outputSqliteFile);
                    }

                    destinationConnection = new SqliteConnection(sqliteConnectionString);
                    schemaWriter = new SQLiteWriter();
                    break;
                case DatabaseEngine.PostgreSql:
                    destinationConnection = new NpgsqlConnection(request.DestinationConnectionString);
                    schemaWriter = new PostgreSqlWriter();
                    break;
                default:
                    throw new NotSupportedException($"Unsupported destination dialect: {request.DestinationDialect}");
            }

            var typeService = new TypeMappingService(GetDialects());
            var schemaReader = new SqlServerSchemaReader();
            var dataMigrator = new DataMigrator();
            var schemaSorter = new SchemaSorter();

            // --- 3. Execute the Migration ---
            try
            {
                await using var sqlConnection = new SqlConnection(request.SourceConnectionString);
                await sqlConnection.OpenAsync();
                await destinationConnection.OpenAsync();

                await _progressHandler.SendLogAsync($"Successfully connected to source ({request.SourceDialect}) and destination ({request.DestinationDialect}).");

                // [PHASE 1] Read and Create Base Schema
                await _progressHandler.SendLogAsync("\n[Phase 1/3] Reading and creating base schema...");
                List<TableSchema> tables = await schemaReader.GetTablesAsync(sqlConnection);

                // 2. Sort the tables before using them for schema creation or data migration.
                tables = schemaSorter.Sort(tables);


                List<ViewSchema> views = await schemaReader.GetViewsAsync(sqlConnection);


                await _progressHandler.SendLogAsync($"Found {tables.Count} tables and {views.Count} views to translate.");
                await schemaWriter.WriteSchemaAsync(destinationConnection, tables, typeService, request.SourceDialect.ToString());
                await schemaWriter.WriteViewsAsync(destinationConnection, views);
                await _progressHandler.SendLogAsync("[Phase 1/3] Base schema creation complete.");

                // [PHASE 2] Migrate Data
                await _progressHandler.SendLogAsync("\n[Phase 2/3] Migrating data...");
                await dataMigrator.MigrateDataAsync(sqlConnection, destinationConnection, tables,

                    async (tableName, rows) => // Use an async lambda to call the async handler
                    {
                        await _progressHandler.SendProgressAsync(tableName, Convert.ToInt32(rows));
                    });
                await _progressHandler.SendLogAsync("\n[Phase 2/3] Data migration complete.");

                // [PHASE 3] Apply Indexes and Constraints
                await _progressHandler.SendLogAsync("\n[Phase 3/3] Applying indexes and constraints...");
                // Note: You will need to update the writer to accept the progress handler
                // so it can report its own warnings.
                await schemaWriter.WriteConstraintsAndIndexesAsync(destinationConnection, _progressHandler);
                await _progressHandler.SendLogAsync("[Phase 3/3] Indexes and constraints applied.");

                await _progressHandler.SendSuccessAsync("✅ Migration completed successfully!");
            }
            catch (Exception ex)
            {
                await _progressHandler.SendFailureAsync(ex.Message, ex.ToString());
                // Re-throw the exception so the calling background task knows it failed.
                throw;
            }
            finally
            {
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
}