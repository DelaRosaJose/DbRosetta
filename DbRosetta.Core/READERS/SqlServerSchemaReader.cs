using System.Data.Common;
using Microsoft.Data.SqlClient;

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
                tables.Add(new TableSchema
                {
                    TableSchemaName = reader.GetString(0),
                    TableName = reader.GetString(1)
                });
            }
        }

        foreach (var table in tables)
        {
            table.Columns = await GetColumnsForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.PrimaryKey = await GetPrimaryKeyForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.ForeignKeys = await GetForeignKeysForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.Indexes = await GetIndexesForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.CheckConstraints = await GetCheckConstraintsForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.UniqueConstraints = await GetUniqueConstraintsForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.Triggers = await GetTriggersForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
        }

        return tables;
    }

    private async Task<List<ColumnSchema>> GetColumnsForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var columns = new List<ColumnSchema>();
        // ENHANCEMENT: Added COLUMNPROPERTY to check for the 'IsIdentity' flag.
        var command = new SqlCommand($@"
                    SELECT
                        COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                        IS_NULLABLE, COLUMN_DEFAULT, NUMERIC_PRECISION, NUMERIC_SCALE,
                        COLUMNPROPERTY(object_id('[{tableSchema}].[{tableName}]'), COLUMN_NAME, 'IsIdentity') as IsIdentity
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = @TableSchema
                    ORDER BY ORDINAL_POSITION", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

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
                    Scale = reader["NUMERIC_SCALE"] != DBNull.Value ? Convert.ToInt32(reader["NUMERIC_SCALE"]) : 0,
                    IsIdentity = reader["IsIdentity"] != DBNull.Value && Convert.ToInt32(reader["IsIdentity"]) == 1
                });
            }
        }
        return columns;
    }

    private async Task<List<string>> GetPrimaryKeyForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var primaryKeys = new List<string>();
        var command = new SqlCommand($"EXEC sp_pkeys @table_name = '{tableName}', @table_owner = '{tableSchema}'", connection);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                primaryKeys.Add(reader["COLUMN_NAME"].ToString()!);
            }
        }
        return primaryKeys;
    }

    private async Task<List<UniqueConstraintSchema>> GetUniqueConstraintsForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var constraints = new Dictionary<string, UniqueConstraintSchema>();
        var command = new SqlCommand(@"
                    SELECT
                        tc.CONSTRAINT_NAME,
                        kcu.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kcu
                        ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                        AND tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'UNIQUE'
                      AND tc.TABLE_NAME = @TableName
                      AND tc.TABLE_SCHEMA = @TableSchema
                    ORDER BY tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;", connection);

        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string constraintName = reader["CONSTRAINT_NAME"].ToString()!;
                if (!constraints.ContainsKey(constraintName))
                {
                    constraints[constraintName] = new UniqueConstraintSchema { ConstraintName = constraintName };
                }
                constraints[constraintName].Columns.Add(reader["COLUMN_NAME"].ToString()!);
            }
        }
        return constraints.Values.ToList();
    }

    private async Task<List<TriggerSchema>> GetTriggersForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var triggers = new List<TriggerSchema>();
        var command = new SqlCommand(@"
                SELECT
                    tr.name AS TriggerName,
                    sm.definition AS TriggerBody,
                    CASE WHEN OBJECTPROPERTY(tr.object_id, 'ExecIsUpdateTrigger') = 1 THEN 'Update' ELSE '' END AS IsUpdate,
                    CASE WHEN OBJECTPROPERTY(tr.object_id, 'ExecIsDeleteTrigger') = 1 THEN 'Delete' ELSE '' END AS IsDelete,
                    CASE WHEN OBJECTPROPERTY(tr.object_id, 'ExecIsInsertTrigger') = 1 THEN 'Insert' ELSE '' END AS IsInsert,
                    CASE WHEN OBJECTPROPERTY(tr.object_id, 'ExecIsInsteadOfTrigger') = 1 THEN 'InsteadOf' ELSE 'After' END AS TriggerType
                FROM sys.triggers AS tr
                JOIN sys.objects AS o ON tr.parent_id = o.object_id
                JOIN sys.sql_modules AS sm ON tr.object_id = sm.object_id
                WHERE o.name = @TableName AND SCHEMA_NAME(o.schema_id) = @TableSchema;", connection);

        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                // A trigger can be for multiple events (e.g., FOR INSERT, UPDATE)
                var events = new[] { reader["IsInsert"].ToString(), reader["IsUpdate"].ToString(), reader["IsDelete"].ToString() };
                foreach (var ev in events)
                {
                    if (string.IsNullOrEmpty(ev)) continue;

                    triggers.Add(new TriggerSchema
                    {
                        Name = reader["TriggerName"].ToString()!,
                        Table = tableName,
                        Body = reader["TriggerBody"].ToString()!,
                        Event = (TriggerEvent)Enum.Parse(typeof(TriggerEvent), ev),
                        Type = (TriggerType)Enum.Parse(typeof(TriggerType), reader["TriggerType"].ToString()!)
                    });
                }
            }
        }
        return triggers;
    }

    // Other methods (GetForeignKeysForTableAsync, etc.) remain the same...
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
                    if (indexDescription.Contains("primary key") || indexDescription.Contains("unique constraint"))
                    {
                        continue;
                    }
                    var index = new IndexSchema { IndexName = reader["index_name"].ToString()!, IsUnique = indexDescription.Contains("unique") };
                    string keyString = reader["index_keys"].ToString()!;
                    var keyParts = keyString.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var keyPart in keyParts)
                    {
                        var col = new IndexColumn();
                        if (keyPart.Contains("(-)"))
                        {
                            col.ColumnName = keyPart.Replace("(-)", "").Trim(); col.IsAscending = false;
                        }
                        else
                        {
                            col.ColumnName = keyPart.Trim(); col.IsAscending = true;
                        }
                        index.Columns.Add(col);
                    }
                    indexes.Add(index);
                }
            }
        }
        catch (SqlException) { /* Ignore */ }
        return indexes;
    }
    private async Task<List<string>> GetCheckConstraintsForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var constraints = new List<string>();
        var command = new SqlCommand(@"
                    SELECT cc.CHECK_CLAUSE FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
                    INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc ON cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
                    WHERE tc.TABLE_NAME = @TableName AND tc.TABLE_SCHEMA = @TableSchema", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader["CHECK_CLAUSE"] != DBNull.Value)
                {
                    constraints.Add(reader["CHECK_CLAUSE"].ToString()!);
                }
            }
        }
        return constraints;
    }
}
