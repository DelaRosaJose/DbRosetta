using DbRosetta.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data.Common;

namespace DbRosetta.Core.Services
{
    public class MigrationService
    {
        private readonly IMigrationProgressHandler _progressHandler;
        private readonly DatabaseProviderFactory _factory;
        private readonly IDataMigrator _dataMigrator;

        public MigrationService(IMigrationProgressHandler progressHandler, DatabaseProviderFactory factory, IDataMigrator dataMigrator)
        {
            _progressHandler = progressHandler;
            _factory = factory;
            _dataMigrator = dataMigrator;
        }

        public async Task ExecuteAsync(MigrationRequest request)
        {
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

            // --- SOURCE SETUP ---
            DbConnection sourceConnection;
            var schemaReader = _factory.GetSchemaReader(request.SourceDialect);
            switch (request.SourceDialect)
            {
                case DatabaseEngine.SqlServer:
                    sourceConnection = new SqlConnection(request.SourceConnectionString);
                    break;
                case DatabaseEngine.PostgreSql:
                    sourceConnection = new NpgsqlConnection(request.SourceConnectionString);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported source dialect: {request.SourceDialect}");
            }

            // --- DESTINATION SETUP ---
            var schemaWriter = _factory.GetSchemaWriter(request.DestinationDialect);
            DbConnection destinationConnection;
            switch (request.DestinationDialect)
            {
                case DatabaseEngine.SQLite:
                    string outputSqliteFile = request.DestinationConnectionString;

                    // --- THIS IS THE FIX ---
                    // Get the directory part of the full file path.
                    string? outputDirectory = Path.GetDirectoryName(outputSqliteFile);
                    if (string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        throw new ArgumentException("Invalid destination file path for SQLite.", nameof(request.DestinationConnectionString));
                    }

                    // Ensure the directory exists. This does nothing if it's already there.
                    Directory.CreateDirectory(outputDirectory);
                    // --- END OF FIX ---

                    // Now that the directory is guaranteed to exist, we can proceed.
                    var sqliteConnectionString = $"Data Source={outputSqliteFile}";
                    if (File.Exists(outputSqliteFile))
                    {
                        File.Delete(outputSqliteFile);
                    }
                    destinationConnection = new SqliteConnection(sqliteConnectionString);
                    break;

                case DatabaseEngine.PostgreSql:
                    destinationConnection = new NpgsqlConnection(request.DestinationConnectionString);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported destination dialect: {request.DestinationDialect}");
            }

            var typeService = new TypeMappingService(GetDialects());
            var schemaSorter = new SchemaSorter();

            try
            {
                await using var sourceConn = sourceConnection;
                // THIS IS THE LINE (75) THAT WAS CRASHING
                await sourceConn.OpenAsync();

                await using var destConn = destinationConnection;
                await destConn.OpenAsync();

                await _progressHandler.SendLogAsync($"Successfully connected to source ({request.SourceDialect}) and destination ({request.DestinationDialect}).");

                // [PHASE 1] Read and Create Base Schema
                await _progressHandler.SendLogAsync("\n[Phase 1/3] Reading and creating base schema...");
                List<TableSchema> tables = await schemaReader.GetTablesAsync(sourceConn);
                tables = schemaSorter.Sort(tables);
                List<ViewSchema> views = await schemaReader.GetViewsAsync(sourceConn);
                await _progressHandler.SendLogAsync($"Found {tables.Count} tables and {views.Count} views to translate.");
                await schemaWriter.WriteSchemaAsync(destConn, tables, typeService, request.SourceDialect.ToString());
                await schemaWriter.WriteViewsAsync(destConn, views);
                await _progressHandler.SendLogAsync("[Phase 1/3] Base schema creation complete.");

                // [PHASE 2] Migrate Data
                await _progressHandler.SendLogAsync("\n[Phase 2/3] Migrating data...");

                // Apply database-specific optimizations before data migration
                await schemaWriter.PreMigrationAsync(destConn, _progressHandler);

                await _dataMigrator.MigrateDataAsync(sourceConn, destConn, tables,
                    async (tableName, rows) =>
                    {
                        await _progressHandler.SendProgressAsync(tableName, (int)rows);
                    });

                // Revert database-specific optimizations after data migration
                await schemaWriter.PostMigrationAsync(destConn, _progressHandler);

                await _progressHandler.SendLogAsync("\n[Phase 2/3] Data migration complete.");

                // [PHASE 3] Apply Indexes and Constraints
                await _progressHandler.SendLogAsync("\n[Phase 3/3] Applying indexes and constraints...");
                await schemaWriter.WriteConstraintsAndIndexesAsync(destConn, _progressHandler);
                await _progressHandler.SendLogAsync("[Phase 3/3] Indexes and constraints applied.");

                await _progressHandler.SendSuccessAsync("✅ Migration completed successfully!");
            }
            catch (Exception ex)
            {
                await _progressHandler.SendFailureAsync(ex.Message, ex.ToString());
                throw;
            }
            finally
            {
                if (sourceConnection != null && sourceConnection.State != System.Data.ConnectionState.Closed)
                {
                    await sourceConnection.CloseAsync();
                }
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
                new SQLiteDialect(),
                new MySqlDialect()
            };
        }

    }
}