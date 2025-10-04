using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace DbRosetta.Core.Reading
{
    public class SqlServerDataReader : IDataReader
    {
        private static readonly HashSet<string> UdtTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "geography",
            "geometry",
            "hierarchyid"
        };

        private static bool IsUdtColumn(string columnType) => UdtTypes.Contains(columnType);

        public async IAsyncEnumerable<UniversalDataRow> ReadDataAsync(DbConnection connection, TableSchema table)
        {
            if (connection is not SqlConnection sqlConnection)
                throw new ArgumentException("Connection must be a SqlConnection for SqlServerDataReader.");

            var quotedTableName = $"[{table.TableSchemaName}].[{table.TableName}]";
            var columnSelections = table.Columns.Select(c =>
                IsUdtColumn(c.ColumnType) ? $"CAST([{c.ColumnName}] AS NVARCHAR(MAX)) AS [{c.ColumnName}]" : $"[{c.ColumnName}]");
            var columnNames = string.Join(", ", columnSelections);
            var query = $"SELECT {columnNames} FROM {quotedTableName}";

            using var command = sqlConnection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new UniversalDataRow();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];
                    object? value;
                    if (reader.IsDBNull(i))
                    {
                        value = null;
                    }
                    else
                    {
                        value = reader.GetValue(i);
                    }

                    // Normalize data: trim strings, handle empty strings, and convert types
                    if (value is string stringValue)
                    {
                        var trimmedValue = stringValue.Trim(); // Trim all whitespace
                        value = string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
                    }
                    else if (value is TimeSpan timeSpanValue)
                    {
                        value = TimeOnly.FromTimeSpan(timeSpanValue);
                    }

                    // For NOT NULL columns, replace null with empty string for strings
                    var baseType = column.ColumnType.ToLower().Split('(')[0];
                    var isStringType = baseType switch
                    {
                        "nvarchar" or "varchar" or "char" or "nchar" or "text" or "ntext" => true,
                        _ => false
                    };
                    if (value == null && !column.IsNullable && isStringType)
                    {
                        value = "";
                    }

                    row.SetValue(column.ColumnName, value);
                }
                yield return row;
            }
        }
    }
}