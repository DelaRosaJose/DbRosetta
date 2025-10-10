using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace DbRosetta.Core.Writers
{
    public class SqlServerDataWriter : IDataWriter
    {
        public async Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data)
        {
            if (connection is not SqlConnection sqlConnection)
                throw new ArgumentException("Connection must be a SqlConnection for SqlServerDataWriter.");

            var dataList = data.ToList();
            if (!dataList.Any()) return;

            var quotedTableName = $"[{table.TableSchemaName}].[{table.TableName}]";
            var columnNames = string.Join(", ", table.Columns.Select(c => $"[{c.ColumnName}]"));

            var valueSets = new List<string>();
            var parameters = new List<SqlParameter>();

            int paramIndex = 0;
            foreach (var row in dataList)
            {
                var paramNames = new List<string>();
                foreach (var column in table.Columns)
                {
                    var paramName = $"@p{paramIndex}";
                    paramNames.Add(paramName);
                    var value = row.GetValue(column.ColumnName);
                    parameters.Add(new SqlParameter(paramName, value ?? DBNull.Value));
                    paramIndex++;
                }
                valueSets.Add($"({string.Join(", ", paramNames)})");
            }

            var insertQuery = $"INSERT INTO {quotedTableName} ({columnNames}) VALUES {string.Join(", ", valueSets)}";

            using var command = sqlConnection.CreateCommand();
            command.CommandText = insertQuery;
            command.Parameters.AddRange(parameters.ToArray());

            await command.ExecuteNonQueryAsync();
        }
    }
}