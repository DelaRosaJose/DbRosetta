using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;
using System.Diagnostics;

namespace DbRosetta.Core.Writers
{
    public class PostgreSqlDataWriter : IDataWriter
    {
        private readonly ILogger<PostgreSqlDataWriter> _logger;
        private readonly int _batchSize;

        public PostgreSqlDataWriter(ILogger<PostgreSqlDataWriter> logger, int batchSize = 10000)
        {
            _logger = logger;
            _batchSize = batchSize;
        }

        public async Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data)
        {
            if (connection is not NpgsqlConnection npgsqlConnection)
                throw new ArgumentException("Connection must be a NpgsqlConnection for PostgreSqlDataWriter.");

            var dataList = data.ToList();
            if (!dataList.Any())
                return;

            var stopwatch = Stopwatch.StartNew();

            var quotedTableName = $"\"public\".\"{table.TableName}\"";
            var columnNames = table.Columns.Select(c => $"\"{c.ColumnName}\"").ToArray();

            using var transaction = await npgsqlConnection.BeginTransactionAsync();
            try
            {
                using (var importer = await npgsqlConnection.BeginBinaryImportAsync($"COPY {quotedTableName} ({string.Join(", ", columnNames)}) FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (var row in dataList)
                    {
                        await importer.StartRowAsync();

                        foreach (var column in table.Columns)
                        {
                            var value = row.GetValue(column.ColumnName);

                            // Apply PostgreSQL-specific transformations
                            if (value is string stringValue)
                            {
                                // Trim whitespace and convert empty strings to null for CHECK constraints
                                var trimmedValue = stringValue.Trim();
                                value = string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
                            }

                            var baseType = column.ColumnType.ToLower().Split('(')[0];

                            // For NOT NULL string columns, replace null with empty string
                            if (value == null && !column.IsNullable)
                            {
                                var isStringType = baseType switch
                                {
                                    "varchar" or "char" or "text" or "character" or "nchar" or "nvarchar" => true,
                                    _ => false
                                };
                                if (isStringType)
                                {
                                    value = "";
                                }
                            }

                            // Handle DBNull
                            if (value == null || value == DBNull.Value)
                            {
                                await importer.WriteNullAsync();
                            }
                            else
                            {
                                // Write value with appropriate type
                                switch (baseType)
                                {
                                    case "xml":
                                        await importer.WriteAsync((string)value, NpgsqlTypes.NpgsqlDbType.Xml);
                                        break;
                                    case "json":
                                        await importer.WriteAsync((string)value, NpgsqlTypes.NpgsqlDbType.Json);
                                        break;
                                    case "jsonb":
                                        await importer.WriteAsync((string)value, NpgsqlTypes.NpgsqlDbType.Jsonb);
                                        break;
                                    case "uuid":
                                        await importer.WriteAsync((Guid)value, NpgsqlTypes.NpgsqlDbType.Uuid);
                                        break;
                                    default:
                                        await importer.WriteAsync(value);
                                        break;
                                }
                            }
                        }
                    }

                    await importer.CompleteAsync();
                }

                await transaction.CommitAsync();
                stopwatch.Stop();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}