using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace DbRosetta.Core.Reading
{
    public class SQLiteDataReader : IDataReader
    {
        public async IAsyncEnumerable<UniversalDataRow> ReadDataAsync(DbConnection connection, TableSchema table)
        {
            if (connection is not SqliteConnection sqliteConnection)
                throw new ArgumentException("Connection must be a SqliteConnection for SQLiteDataReader.");

            var quotedTableName = $"\"{table.TableName}\"";
            var columnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));
            var query = $"SELECT {columnNames} FROM {quotedTableName}";

            using var command = sqliteConnection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new UniversalDataRow();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);

                    // Normalize data: trim strings and handle empty strings
                    if (value is string stringValue)
                    {
                        var trimmedValue = stringValue.Trim(); // Trim all whitespace
                        value = string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
                    }

                    row.SetValue(column.ColumnName, value);
                }
                yield return row;
            }
        }
    }
}