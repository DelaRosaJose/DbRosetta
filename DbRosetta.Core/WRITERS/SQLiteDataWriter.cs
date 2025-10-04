using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace DbRosetta.Core.Writers
{
    public class SQLiteDataWriter : IDataWriter
    {
        private readonly ILogger<SQLiteDataWriter> _logger;

        public SQLiteDataWriter(ILogger<SQLiteDataWriter> logger)
        {
            _logger = logger;
        }

        public async Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data)
        {
            if (connection is not SqliteConnection sqliteConnection)
                throw new ArgumentException("Connection must be a SqliteConnection for SQLiteDataWriter.");

            var dataList = data.ToList();
            if (!dataList.Any())
                return;

            var quotedTableName = $"\"{table.TableName}\"";
            var columnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));
            var parameterNames = string.Join(", ", table.Columns.Select(c => $"@{c.ColumnName.Replace(" ", "_")}"));

            var insertQuery = $"INSERT INTO {quotedTableName} ({columnNames}) VALUES ({parameterNames})";

            using var transaction = await sqliteConnection.BeginTransactionAsync();
            try
            {
                using var command = sqliteConnection.CreateCommand();
                command.CommandText = insertQuery;
                command.Transaction = (SqliteTransaction)transaction;

                // Prepare the command for better performance
                await command.PrepareAsync();

                foreach (var row in dataList)
                {
                    command.Parameters.Clear();

                    var parameterValues = new Dictionary<string, object?>();
                    foreach (var column in table.Columns)
                    {
                        var value = row.GetValue(column.ColumnName);
                        // Apply SQLite-specific transformations
                        if (value is string stringValue)
                        {
                            // Trim whitespace and convert empty strings to null for CHECK constraints
                            var trimmedValue = stringValue.Trim();
                            value = string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
                        }

                        // For NOT NULL string columns, replace null with empty string
                        if (value == null && !column.IsNullable)
                        {
                            var baseType = column.ColumnType.ToLower().Split('(')[0];
                            var isStringType = baseType switch
                            {
                                "nvarchar" or "varchar" or "char" or "nchar" or "text" or "ntext" => true,
                                _ => false
                            };
                            if (isStringType)
                            {
                                value = "";
                            }
                        }

                        command.Parameters.AddWithValue($"@{column.ColumnName.Replace(" ", "_")}", value ?? DBNull.Value);
                        parameterValues[column.ColumnName] = value ?? DBNull.Value;
                    }


                    try
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to insert row into table {TableName}. Parameter values: {@Parameters}", table.TableName, parameterValues);
                        throw;
                    }
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}