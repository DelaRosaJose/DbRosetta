using System.Data.Common;
using Microsoft.Data.SqlClient;

public class SqlServerSchemaReader : IDatabaseSchemaReader
{
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
                tables.Add(new TableSchema
                {
                    TableSchemaName = reader.GetString(0),
                    TableName = reader.GetString(1)
                });
            }
        }

        // Now, for each table, get its detailed schema
        foreach (var table in tables)
        {
            table.Columns = await GetColumnsForTableAsync(sqlConnection, table.TableName);
            table.PrimaryKey = await GetPrimaryKeyForTableAsync(sqlConnection, table.TableName);

            // --- NEW: Read Foreign Keys and Indexes ---
            table.ForeignKeys = await GetForeignKeysForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.Indexes = await GetIndexesForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
        }

        return tables;
    }

    // ... GetColumnsForTableAsync and GetPrimaryKeyForTableAsync methods remain the same ...

    // --- NEW METHOD: Get Foreign Keys ---
    private async Task<List<ForeignKeySchema>> GetForeignKeysForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var foreignKeys = new List<ForeignKeySchema>();
        var command = new SqlCommand(@"
                SELECT 
                    fk.TABLE_NAME AS FkTable,
                    kcu.COLUMN_NAME AS FkColumn,
                    pk.TABLE_NAME AS PkTable,
                    pt.COLUMN_NAME AS PkColumn
                FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS fk ON rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
                JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS pk ON rc.UNIQUE_CONSTRAINT_NAME = pk.CONSTRAINT_NAME
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                JOIN (
                    SELECT i1.TABLE_NAME, i2.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS i1
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE i2 ON i1.CONSTRAINT_NAME = i2.CONSTRAINT_NAME
                    WHERE i1.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) PT ON PT.TABLE_NAME = pk.TABLE_NAME
                WHERE fk.TABLE_NAME = @TableName AND fk.TABLE_SCHEMA = @TableSchema", connection);

        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                foreignKeys.Add(new ForeignKeySchema
                {
                    TableName = reader["FkTable"].ToString()!,
                    ColumnName = reader["FkColumn"].ToString()!,
                    ForeignTableName = reader["PkTable"].ToString()!,
                    ForeignColumnName = reader["PkColumn"].ToString()!
                });
            }
        }
        return foreignKeys;
    }

    // --- NEW METHOD: Get Indexes ---
    private async Task<List<IndexSchema>> GetIndexesForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var indexes = new List<IndexSchema>();
        var command = new SqlCommand($"EXEC sp_helpindex '{tableSchema}.{tableName}'", connection);

        try
        {
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string indexDescription = reader["index_description"].ToString()!;

                    // IMPORTANT: Skip primary key and unique constraints, as they are handled elsewhere.
                    if (indexDescription.Contains("primary key") || indexDescription.Contains("unique constraint"))
                    {
                        continue;
                    }

                    var index = new IndexSchema
                    {
                        IndexName = reader["index_name"].ToString()!,
                        IsUnique = indexDescription.Contains("unique")
                    };

                    string keyString = reader["index_keys"].ToString()!;
                    var keyParts = keyString.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var keyPart in keyParts)
                    {
                        var col = new IndexColumn();
                        // Check if the column is descending
                        if (keyPart.Contains("(-)"))
                        {
                            col.ColumnName = keyPart.Replace("(-)", "").Trim();
                            col.IsAscending = false;
                        }
                        else
                        {
                            col.ColumnName = keyPart.Trim();
                            col.IsAscending = true;
                        }
                        index.Columns.Add(col);
                    }
                    indexes.Add(index);
                }
            }
        }
        catch (SqlException)
        {
            // sp_helpindex can throw an exception if the object isn't a table (e.g., a view).
            // We can safely ignore this and return an empty list.
        }
        return indexes;
    }
}
