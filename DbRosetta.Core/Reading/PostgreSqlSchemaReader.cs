using Npgsql;
using System.Data.Common;

public class PostgreSqlSchemaReader : IDatabaseSchemaReader
{
    private readonly IExpressionParser _parser;

    public PostgreSqlSchemaReader()
    {
        _parser = new PostgreSqlExpressionParser();
    }

    public async Task<List<TableSchema>> GetTablesAsync(DbConnection connection)
    {
        if (!(connection is NpgsqlConnection npgsqlConnection))
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        var tables = new List<TableSchema>();
        var command = new NpgsqlCommand(@"
            SELECT table_schema, table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", npgsqlConnection);

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
            table.Columns = await GetColumnsForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.PrimaryKey = await GetPrimaryKeyForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.ForeignKeys = await GetForeignKeysForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.Indexes = await GetIndexesForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.CheckConstraints = await GetCheckConstraintsForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.UniqueConstraints = await GetUniqueConstraintsForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
            table.Triggers = await GetTriggersForTableAsync(npgsqlConnection, table.TableName, table.TableSchemaName);
        }

        return tables;
    }

    // THIS ENTIRE METHOD WAS MISSING
    public async Task<List<ViewSchema>> GetViewsAsync(DbConnection connection)
    {
        if (connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        var views = new List<ViewSchema>();
        var command = new NpgsqlCommand(@"
            SELECT table_name, view_definition 
            FROM information_schema.views 
            WHERE table_schema = 'public'", npgsqlConnection);

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                views.Add(new ViewSchema
                {
                    ViewName = reader["table_name"].ToString()!,
                    ViewSQL = reader["view_definition"].ToString()!
                });
            }
        }
        return views;
    }

    private async Task<List<ColumnSchema>> GetColumnsForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var columns = new List<ColumnSchema>();
        var commandText = @"
            SELECT column_name, udt_name, character_maximum_length, is_nullable, 
                   column_default, numeric_precision, numeric_scale
            FROM information_schema.columns 
            WHERE table_name = @TableName AND table_schema = @TableSchema
            ORDER BY ordinal_position";
        var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var column = new ColumnSchema
                {
                    ColumnName = reader["column_name"].ToString()!,
                    ColumnType = reader["udt_name"].ToString()!,
                    Length = reader["character_maximum_length"] != DBNull.Value ? Convert.ToInt32(reader["character_maximum_length"]) : 0,
                    IsNullable = "YES".Equals(reader["is_nullable"].ToString(), StringComparison.OrdinalIgnoreCase),
                    Precision = reader["numeric_precision"] != DBNull.Value ? Convert.ToInt32(reader["numeric_precision"]) : 0,
                    Scale = reader["numeric_scale"] != DBNull.Value ? Convert.ToInt32(reader["numeric_scale"]) : 0,
                    IsIdentity = (reader["column_default"]?.ToString() ?? string.Empty).StartsWith("nextval(")
                };
                string defaultString = reader["column_default"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(defaultString))
                {
                    column.DefaultValueAst = _parser.Parse(defaultString);
                    column.DefaultValueAsString = GenerateSqlForNode(column.DefaultValueAst);
                }
                columns.Add(column);
            }
        }
        return columns;
    }

    private async Task<List<string>> GetPrimaryKeyForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var primaryKeys = new List<string>();
        var command = new NpgsqlCommand(@"
            SELECT kcu.column_name FROM information_schema.table_constraints AS tc 
            JOIN information_schema.key_column_usage AS kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_name = @TableName AND tc.table_schema = @TableSchema
            ORDER BY kcu.ordinal_position;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                primaryKeys.Add(reader["column_name"].ToString()!);
            }
        }
        return primaryKeys;
    }

    private async Task<List<ForeignKeySchema>> GetForeignKeysForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var foreignKeysMap = new Dictionary<string, ForeignKeySchema>();
        var command = new NpgsqlCommand(@"
            SELECT tc.constraint_name, kcu.table_name AS local_table, kcu.column_name AS local_column,
                   ccu.table_name AS foreign_table, ccu.column_name AS foreign_column,
                   rc.update_rule AS update_action, rc.delete_rule AS delete_action
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage AS ccu ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
            JOIN information_schema.referential_constraints AS rc ON rc.constraint_name = tc.constraint_name AND rc.constraint_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY' AND kcu.table_name = @TableName AND kcu.table_schema = @TableSchema
            ORDER BY tc.constraint_name, kcu.ordinal_position;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string fkName = reader["constraint_name"].ToString()!;
                if (!foreignKeysMap.ContainsKey(fkName))
                {
                    foreignKeysMap[fkName] = new ForeignKeySchema
                    {
                        ForeignKeyName = fkName,
                        LocalTable = reader["local_table"].ToString()!,
                        ForeignTable = reader["foreign_table"].ToString()!,
                        UpdateAction = reader["update_action"].ToString()!,
                        DeleteAction = reader["delete_action"].ToString()!
                    };
                }
                foreignKeysMap[fkName].LocalColumns.Add(reader["local_column"].ToString()!);
                foreignKeysMap[fkName].ForeignColumns.Add(reader["foreign_column"].ToString()!);
            }
        }
        return foreignKeysMap.Values.ToList();
    }

    private async Task<List<IndexSchema>> GetIndexesForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var indexMap = new Dictionary<string, IndexSchema>();
        var command = new NpgsqlCommand(@"
            SELECT i.relname as indexname, idx.indisunique as isunique, a.attname as columnname,
                   (SELECT indoption[array_position(idx.indkey, a.attnum) - 1] & 1) = 1 AS isdescending
            FROM pg_class t, pg_class i, pg_index idx, pg_attribute a, pg_namespace n
            WHERE t.oid = idx.indrelid AND i.oid = idx.indexrelid AND a.attrelid = t.oid
              AND a.attnum = ANY(idx.indkey) AND t.relkind = 'r' AND t.relname = @TableName
              AND n.oid = t.relnamespace AND n.nspname = @TableSchema AND idx.indisprimary = false AND idx.indisunique = false;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string indexName = reader["indexname"].ToString()!;
                if (!indexMap.ContainsKey(indexName))
                {
                    var isUniqueValue = reader["isunique"];
                    var isUnique = (isUniqueValue != DBNull.Value) && Convert.ToBoolean(isUniqueValue);
                    indexMap[indexName] = new IndexSchema { IndexName = indexName, IsUnique = isUnique, Columns = new List<IndexColumn>() };
                }
                var isDescendingValue = reader["isdescending"];
                var isDescending = (isDescendingValue != DBNull.Value) && Convert.ToBoolean(isDescendingValue);
                indexMap[indexName].Columns.Add(new IndexColumn { ColumnName = reader["columnname"].ToString()!, IsAscending = !isDescending });
            }
        }
        return indexMap.Values.ToList();
    }

    private async Task<List<CheckConstraintSchema>> GetCheckConstraintsForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var constraints = new List<CheckConstraintSchema>();
        var command = new NpgsqlCommand(@"
            SELECT con.conname AS constraint_name, pg_get_constraintdef(con.oid) AS check_clause
            FROM pg_constraint con
            JOIN pg_class t ON t.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE t.relname = @TableName AND n.nspname = @TableSchema AND con.contype = 'c'", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var constraint = new CheckConstraintSchema { ConstraintName = reader["constraint_name"].ToString()! };
                string clauseString = reader["check_clause"].ToString()!;
                constraint.CheckClauseAst = _parser.Parse(clauseString);
                constraint.CheckClauseAsString = GenerateSqlForNode(constraint.CheckClauseAst);
                constraints.Add(constraint);
            }
        }
        return constraints;
    }

    private async Task<List<UniqueConstraintSchema>> GetUniqueConstraintsForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var constraints = new Dictionary<string, UniqueConstraintSchema>();
        var command = new NpgsqlCommand(@"
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'UNIQUE' AND tc.table_name = @TableName AND tc.table_schema = @TableSchema
            ORDER BY tc.constraint_name, kcu.ordinal_position;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string constraintName = reader["constraint_name"].ToString()!;
                if (!constraints.ContainsKey(constraintName))
                {
                    constraints[constraintName] = new UniqueConstraintSchema { ConstraintName = constraintName };
                }
                constraints[constraintName].Columns.Add(reader["column_name"].ToString()!);
            }
        }
        return constraints.Values.ToList();
    }

    private async Task<List<TriggerSchema>> GetTriggersForTableAsync(NpgsqlConnection connection, string tableName, string tableSchema)
    {
        var triggers = new List<TriggerSchema>();
        var command = new NpgsqlCommand(@"
            SELECT t.tgname AS trigger_name, p.prosrc AS trigger_body,
                   CASE WHEN (t.tgtype::integer & 4) <> 0 THEN 'Insert' WHEN (t.tgtype::integer & 8) <> 0 THEN 'Delete' WHEN (t.tgtype::integer & 16) <> 0 THEN 'Update' ELSE 'Unknown' END AS trigger_event,
                   CASE WHEN (t.tgtype::integer & 1) <> 0 OR (t.tgtype::integer & 64) <> 0 THEN 'InsteadOf' WHEN (t.tgtype::integer & 2) <> 0 THEN 'Before' ELSE 'After' END AS trigger_type
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_proc p ON p.oid = t.tgfoid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relname = @TableName AND n.nspname = @TableSchema AND NOT t.tgisinternal", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@TableSchema", tableSchema);
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (Enum.TryParse<TriggerEvent>(reader["trigger_event"].ToString(), true, out var triggerEvent) &&
                    Enum.TryParse<TriggerType>(reader["trigger_type"].ToString(), true, out var triggerType))
                {
                    triggers.Add(new TriggerSchema
                    {
                        Name = reader["trigger_name"].ToString()!,
                        Table = tableName,
                        Body = reader["trigger_body"].ToString()!,
                        Event = triggerEvent,
                        Type = triggerType
                    });
                }
            }
        }
        return triggers;
    }

    /// <summary>
    /// Generates SQLite-compatible SQL syntax from the universal AST.
    /// </summary>
    private string GenerateSqlForNode(ExpressionNode node)
    {
        return node switch
        {
            FunctionCallNode func => func.UniversalFunctionName switch
            {
                "GetCurrentTimestamp" => "CURRENT_TIMESTAMP",
                "GenerateUuid" => "(lower(hex(randomblob(16))))",
                "upper" => $"UPPER({GenerateSqlForNode(func.Arguments[0])})",
                "dateadd" => GenerateDateAddSql(func),
                _ => throw new NotSupportedException($"Unsupported universal function: {func.UniversalFunctionName}")
            },
            LiteralNode literal => literal.Value switch
            {
                string s when IsExpressionString(s) => s.Replace("::text", "").Replace("::character varying", "").Replace("::varchar", ""),
                string s => $"'{s.Replace("'", "''")}'",
                bool b => b ? "1" : "0",
                null => "NULL",
                _ => literal.Value?.ToString() ?? "NULL"
            },
            IdentifierNode identifier => $"[{identifier.Name}]",
            OperatorNode op => GenerateOperatorSql(op),
            _ => throw new NotSupportedException($"Unsupported ExpressionNode type: {node.GetType().Name}")
        };
    }

    private bool IsExpressionString(string s)
    {
        return s.Contains(" OR ") || s.Contains(" IS ") || s.Contains(" AND ") || s.Contains("=");
    }

    private string GenerateOperatorSql(OperatorNode op)
    {
        if (op.Operator.Equals("IN", StringComparison.OrdinalIgnoreCase) && op.Left is IdentifierNode identifier)
        {
            // For IN expressions, add NULL handling: (column IS NULL OR column IN (...))
            return $"({GenerateSqlForNode(op.Left!)} IS NULL OR {GenerateSqlForNode(op.Left!)} {op.Operator} {GenerateSqlForNode(op.Right!)})";
        }
        if (op.Operator.Equals("LIKE", StringComparison.OrdinalIgnoreCase))
        {
            // Make LIKE case insensitive
            return $"({GenerateSqlForNode(op.Left!)} LIKE {GenerateSqlForNode(op.Right!)} COLLATE NOCASE)";
        }
        return $"({GenerateSqlForNode(op.Left!)} {op.Operator} {GenerateSqlForNode(op.Right!)})";
    }

    private string GenerateDateAddSql(FunctionCallNode func)
    {
        if (func.Arguments.Count != 3) throw new ArgumentException("DateAdd requires 3 arguments");
        string part = (func.Arguments[0] as LiteralNode)?.Value as string ?? throw new ArgumentException("First argument must be a string literal");
        var numberNode = func.Arguments[1] as LiteralNode;
        if (numberNode?.Value is not (int or long or decimal)) throw new ArgumentException("Second argument must be numeric");
        int number = Convert.ToInt32(numberNode.Value);
        string dateSql = GenerateSqlForNode(func.Arguments[2]);
        string modifier = $"{(number >= 0 ? "+" : "")}{number} {MapDatePart(part)}";
        return $"date({dateSql}, '{modifier}')";
    }

    private string MapDatePart(string part) => part.ToLowerInvariant() switch
    {
        "year" or "yy" or "yyyy" => "year",
        "month" or "mm" or "m" => "month",
        "day" or "dd" or "d" => "day",
        "hour" or "hh" => "hour",
        "minute" or "mi" or "n" => "minute",
        "second" or "ss" or "s" => "second",
        _ => throw new NotSupportedException($"Unsupported date part: {part}")
    };
}