using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DbRosetta.Core.Services
{
    public class DataMigratorService : IDataMigrator
    {
        private readonly DatabaseProviderFactory _factory;
        private readonly ILogger<DataMigratorService> _logger;
        private const int DefaultBatchSize = 10000;
        private const int SQLiteBatchSize = 10000;
        private const int ProgressReportBatchSize = 500;

        public DataMigratorService(DatabaseProviderFactory factory, ILogger<DataMigratorService> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task MigrateDataAsync(
            DbConnection sourceConnection,
            DbConnection destinationConnection,
            List<TableSchema> tables,
            Func<string, int, Task> progressAction)
        {
            try
            {
                _logger.LogInformation("Starting data migration for {TableCount} tables.", tables.Count);

                // Determine source and destination engines
                var sourceEngine = GetEngineFromConnection(sourceConnection);
                var destinationEngine = GetEngineFromConnection(destinationConnection);

                var dataReader = _factory.GetDataReader(sourceEngine);
                var dataWriter = _factory.GetDataWriter(destinationEngine);

                // Set batch size based on destination engine for optimal performance
                var batchSize = destinationEngine == DatabaseEngine.SQLite ? SQLiteBatchSize : DefaultBatchSize;

                foreach (var table in tables)
                {
                    if (!table.Columns.Any())
                    {
                        _logger.LogWarning("Skipping table {TableName} as it has no columns.", table.TableName);
                        continue;
                    }

                    try
                    {
                        _logger.LogInformation("Migrating data for table {TableName}.", table.TableName);

                        var totalRows = 0;
                        var batch = new List<UniversalDataRow>();
                        var readStopwatch = Stopwatch.StartNew();
                        await foreach (var row in dataReader.ReadDataAsync(sourceConnection, table))
                        {
                            batch.Add(row);
                            if (batch.Count >= batchSize)
                            {
                                var writeStopwatch = Stopwatch.StartNew();
                                await dataWriter.WriteDataAsync(destinationConnection, table, batch);
                                writeStopwatch.Stop();
                                _logger.LogInformation("Wrote batch of {BatchSize} rows for table {TableName} in {ElapsedMs} ms.", batch.Count, table.TableName, writeStopwatch.ElapsedMilliseconds);
                                totalRows += batch.Count;
                                batch.Clear();
                            }
                        }
                        readStopwatch.Stop();
                        _logger.LogInformation("Read all rows for table {TableName} in {ElapsedMs} ms.", table.TableName, readStopwatch.ElapsedMilliseconds);

                        // Write remaining batch
                        if (batch.Count > 0)
                        {
                            var writeStopwatch = Stopwatch.StartNew();
                            await dataWriter.WriteDataAsync(destinationConnection, table, batch);
                            writeStopwatch.Stop();
                            _logger.LogInformation("Wrote final batch of {BatchSize} rows for table {TableName} in {ElapsedMs} ms.", batch.Count, table.TableName, writeStopwatch.ElapsedMilliseconds);
                            totalRows += batch.Count;
                        }

                        await progressAction(table.TableName, totalRows);

                        _logger.LogInformation("Migrated {RowCount} rows for table {TableName}.", totalRows, table.TableName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error migrating data for table {TableName}.", table.TableName);
                        throw new DatabaseMigrationException($"Failed to migrate data for table {table.TableName}.", ex);
                    }
                }

                _logger.LogInformation("Data migration completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data migration failed.");
                throw;
            }
        }

        private DatabaseEngine GetEngineFromConnection(DbConnection connection)
        {
            return connection switch
            {
                SqlConnection => DatabaseEngine.SqlServer,
                NpgsqlConnection => DatabaseEngine.PostgreSql,
                SqliteConnection => DatabaseEngine.SQLite,
                _ => throw new NotSupportedException($"Unsupported connection type: {connection.GetType().Name}")
            };
        }

    }
}