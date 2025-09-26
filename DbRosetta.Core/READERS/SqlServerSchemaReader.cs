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

            // Fetch and populate Foreign Keys
            table.ForeignKeys = await GetForeignKeysForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);

            table.Indexes = await GetIndexesForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);

            // Fetch and populate Check Constraints
            table.CheckConstraints = await GetCheckConstraintsForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);

            table.UniqueConstraints = await GetUniqueConstraintsForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
            table.Triggers = await GetTriggersForTableAsync(sqlConnection, table.TableName, table.TableSchemaName);
        }

        return tables;
    }

    public async Task<List<ViewSchema>> GetViewsAsync(DbConnection connection)
    {
        if (!(connection is SqlConnection sqlConnection))
        {
            throw new ArgumentException("A SqlConnection is required.", nameof(connection));
        }

        var views = new List<ViewSchema>();
        var command = new SqlCommand("SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS", sqlConnection);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                views.Add(new ViewSchema
                {
                    ViewName = reader["TABLE_NAME"].ToString()!,
                    ViewSQL = reader["VIEW_DEFINITION"].ToString()!
                });
            }
        }
        return views;
    }

    private async Task<List<ColumnSchema>> GetColumnsForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var columns = new List<ColumnSchema>();
        var commandText = $@"
                SELECT
                    COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                    IS_NULLABLE, COLUMN_DEFAULT, NUMERIC_PRECISION, NUMERIC_SCALE,
                    COLUMNPROPERTY(object_id('[{tableSchema}].[{tableName}]'), COLUMN_NAME, 'IsIdentity') as IsIdentity
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = @TableSchema
                ORDER BY ORDINAL_POSITION";
        var command = new SqlCommand(commandText, connection);
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
                    IsNullable = "YES".Equals(reader["IS_NULLABLE"].ToString(), StringComparison.OrdinalIgnoreCase),
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

    /// <summary>
    /// Retrieves foreign key constraints for a given table, correctly handling composite keys and referential actions.
    /// </summary>
    private async Task<List<ForeignKeySchema>> GetForeignKeysForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        // Use a Dictionary to group columns by the foreign key's name.
        var foreignKeysMap = new Dictionary<string, ForeignKeySchema>();

        var command = new SqlCommand(@"
            SELECT
                fk.name AS ForeignKeyName,
                tp.name AS LocalTable,
                cp.name AS LocalColumn,
                tr.name AS ForeignTable,
                cr.name AS ForeignColumn,
                fk.delete_referential_action_desc AS DeleteAction,
                fk.update_referential_action_desc AS UpdateAction,
                SCHEMA_NAME(tp.schema_id) as LocalTableSchema -- Include schema for accurate matching if needed
            FROM
                sys.foreign_keys AS fk
            INNER JOIN sys.tables AS tp ON fk.parent_object_id = tp.object_id
            INNER JOIN sys.tables AS tr ON fk.referenced_object_id = tr.object_id
            INNER JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns AS cp ON fkc.parent_column_id = cp.column_id AND fkc.parent_object_id = cp.object_id
            INNER JOIN sys.columns AS cr ON fkc.referenced_column_id = cr.column_id AND fkc.referenced_object_id = cr.object_id
            WHERE tp.name = @TableName AND SCHEMA_NAME(tp.schema_id) = @TableSchema
            ORDER BY
                fk.name, fkc.constraint_column_id;", connection);

        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string fkName = reader["ForeignKeyName"].ToString()!;

                if (!foreignKeysMap.ContainsKey(fkName))
                {
                    foreignKeysMap[fkName] = new ForeignKeySchema
                    {
                        ForeignKeyName = fkName,
                        LocalTable = reader["LocalTable"].ToString()!,
                        ForeignTable = reader["ForeignTable"].ToString()!,
                        DeleteAction = reader["DeleteAction"].ToString()!.Replace("_", " "), // Normalize action names
                        UpdateAction = reader["UpdateAction"].ToString()!.Replace("_", " ")  // Normalize action names
                    };
                }
                foreignKeysMap[fkName].LocalColumns.Add(reader["LocalColumn"].ToString()!);
                foreignKeysMap[fkName].ForeignColumns.Add(reader["ForeignColumn"].ToString()!);
            }
        }
        return foreignKeysMap.Values.ToList();
    }


    private async Task<List<IndexSchema>> GetIndexesForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var indexes = new List<IndexSchema>();
        // Using sys.indexes and sys.index_columns for more robust index information
        var command = new SqlCommand(@"
            SELECT
                i.name AS IndexName,
                i.is_unique AS IsUnique,
                c.name AS ColumnName,
                ic.is_descending_key AS IsDescending
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = OBJECT_ID(@TableNameWithSchema)
              AND i.is_primary_key = 0 -- Exclude primary key indexes
              AND i.is_unique_constraint = 0 -- Exclude unique constraints (handled separately)
            ORDER BY i.name, ic.key_ordinal;", connection);

        command.Parameters.AddWithValue("@TableNameWithSchema", $"[{tableSchema}].[{tableName}]");

        var indexMap = new Dictionary<string, IndexSchema>();

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string indexName = reader["IndexName"].ToString()!;
                if (!indexMap.ContainsKey(indexName))
                {
                    indexMap[indexName] = new IndexSchema
                    {
                        IndexName = indexName,
                        IsUnique = (bool)reader["IsUnique"],
                        Columns = new List<IndexColumn>()
                    };
                }
                indexMap[indexName].Columns.Add(new IndexColumn
                {
                    ColumnName = reader["ColumnName"].ToString()!,
                    IsAscending = !(bool)reader["IsDescending"]
                });
            }
        }
        return indexMap.Values.ToList();
    }


    /// <summary>
    /// Retrieves CHECK constraints for a given table, populating CheckConstraintSchema objects.
    /// </summary>
    private async Task<List<CheckConstraintSchema>> GetCheckConstraintsForTableAsync(SqlConnection connection, string tableName, string tableSchema)
    {
        var constraints = new List<CheckConstraintSchema>();
        var command = new SqlCommand(@"
            SELECT
                tc.CONSTRAINT_NAME,
                cc.CHECK_CLAUSE
            FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
            INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                ON cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                AND cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
            WHERE tc.TABLE_NAME = @TableName AND tc.TABLE_SCHEMA = @TableSchema;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader["CHECK_CLAUSE"] != DBNull.Value)
                {
                    constraints.Add(new CheckConstraintSchema
                    {
                        ConstraintName = reader["CONSTRAINT_NAME"].ToString()!,
                        CheckClause = reader["CHECK_CLAUSE"].ToString()!
                    });
                }
            }
        }
        return constraints;
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
        //using (var reader = await command.ExecuteReaderAsync())
        //{
        //    while (await reader.ReadAsync())
        //    {
        //        // A trigger can be for multiple events (e.g., FOR INSERT, UPDATE)
        //        // We need to create a separate TriggerSchema for each event if they are distinct events
        //        // However, often SQL Server triggers are defined for multiple events in one go (e.g., FOR INSERT, UPDATE)
        //        // For simplicity here, we'll assume one TriggerSchema object per unique event type found in the T-SQL.
        //        // If a single T-SQL trigger body handles multiple events, this might need refinement.

        //        string triggerType = reader["TriggerType"].ToString()!; // "InsteadOf" or "After"
        //        TriggerType parsedTriggerType = (TriggerType)Enum.Parse(typeof(TriggerType), triggerType);

        //        if ("1".Equals(reader["IsInsert"].ToString()))
        //        {
        //            triggers.Add(new TriggerSchema
        //            {
        //                Name = reader["TriggerName"].ToString()!,
        //                Table = tableName,
        //                Body = reader["TriggerBody"].ToString()!,
        //                Event = TriggerEvent.Insert,
        //                Type = parsedTriggerType
        //            });
        //        }
        //        if ("1".Equals(reader["IsUpdate"].ToString()))
        //        {
        //            triggers.Add(new TriggerSchema
        //            {
        //                Name = reader["TriggerName"].ToString()!,
        //                Table = tableName,
        //                Body = reader["TriggerBody"].ToString()!,
        //                Event = TriggerEvent.Update,
        //                Type = parsedTriggerType
        //            });
        //        }
        //        if ("1".Equals(reader["IsDelete"].ToString()))
        //        {
        //            triggers.Add(new TriggerSchema
        //            {
        //                Name = reader["TriggerName"].ToString()!,
        //                Table = tableName,
        //                Body = reader["TriggerBody"].ToString()!,
        //                Event = TriggerEvent.Delete,
        //                Type = parsedTriggerType
        //            });
        //        }
        //    }
        //}
        return triggers;
    }
}