using Microsoft.Data.SqlClient;
using System.Data.Common;

public class SqlServerSchemaReader : IDatabaseSchemaReader
    {
        public async Task<List<TableSchema>> GetTablesAsync(DbConnection connection)
        {
            if (!(connection is SqlConnection sqlConnection))
            {
                throw new ArgumentException("A SqlConnection is required.", nameof(connection));
            }

            var tables = new List<TableSchema>();
            var command = new SqlCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", sqlConnection);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var tableSchema = new TableSchema
                    {
                        TableSchemaName = reader.GetString(0),
                        TableName = reader.GetString(1)
                    };
                    tables.Add(tableSchema);
                }
            }

            // Now, for each table, get its detailed schema
            foreach (var table in tables)
            {
                table.Columns = await GetColumnsForTableAsync(sqlConnection, table.TableName);
                table.PrimaryKey = await GetPrimaryKeyForTableAsync(sqlConnection, table.TableName);
                // Future work: Get Foreign Keys and Indexes
            }

            return tables;
        }

        private async Task<List<ColumnSchema>> GetColumnsForTableAsync(SqlConnection connection, string tableName)
        {
            // Same as before
            var columns = new List<ColumnSchema>();
            var command = new SqlCommand(@"
                SELECT 
                    COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                    IS_NULLABLE, COLUMN_DEFAULT, NUMERIC_PRECISION, NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName
                ORDER BY ORDINAL_POSITION", connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(new ColumnSchema
                    {
                        ColumnName = reader["COLUMN_NAME"].ToString()!,
                        ColumnType = reader["DATA_TYPE"].ToString()!,
                        Length = reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value ? Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]) : 0,
                        IsNullable = reader["IS_NULLABLE"].ToString()!.Equals("YES", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = reader["COLUMN_DEFAULT"]?.ToString() ?? string.Empty,
                        Precision = reader["NUMERIC_PRECISION"] != DBNull.Value ? Convert.ToInt32(reader["NUMERIC_PRECISION"]) : 0,
                        Scale = reader["NUMERIC_SCALE"] != DBNull.Value ? Convert.ToInt32(reader["NUMERIC_SCALE"]) : 0
                    });
                }
            }
            return columns;
        }

        private async Task<List<string>> GetPrimaryKeyForTableAsync(SqlConnection connection, string tableName)
        {
            var primaryKeys = new List<string>();
            // sp_pkeys is a legacy but simple way to get PKs
            var command = new SqlCommand($"EXEC sp_pkeys '{tableName}'", connection);
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    primaryKeys.Add(reader["COLUMN_NAME"].ToString()!);
                }
            }
            return primaryKeys;
        }
    }
