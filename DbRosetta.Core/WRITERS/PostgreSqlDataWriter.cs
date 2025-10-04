using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Npgsql;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace DbRosetta.Core.Writers
{
    public class PostgreSqlDataWriter : IDataWriter
    {
        public async Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data)
        {
            if (connection is not NpgsqlConnection npgsqlConnection)
                throw new ArgumentException("Connection must be a NpgsqlConnection for PostgreSqlDataWriter.");

            var quotedTableName = $"\"{table.TableSchemaName}\".\"{table.TableName}\"";
            var columnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));
            var parameterNames = string.Join(", ", table.Columns.Select(c => $"@{c.ColumnName}"));

            var insertQuery = $"INSERT INTO {quotedTableName} ({columnNames}) VALUES ({parameterNames})";

            foreach (var row in data)
            {
                using var command = npgsqlConnection.CreateCommand();
                command.CommandText = insertQuery;

                foreach (var column in table.Columns)
                {
                    var value = row.GetValue(column.ColumnName);
                    command.Parameters.AddWithValue($"@{column.ColumnName}", value ?? DBNull.Value);
                }

                await command.ExecuteNonQueryAsync();
            }
        }
    }
}