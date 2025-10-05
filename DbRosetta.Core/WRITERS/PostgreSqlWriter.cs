
using DbRosetta.Core;
using Npgsql;
using System.Data.Common;
using System.Text;

public class PostgreSqlWriter : IDatabaseSchemaWriter
{
    private List<TableSchema>? _tables;
    private TypeMappingService? _typeService;
    private string? _sourceDialectName;

    public async Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName)
    {
        _tables = tables;
        _typeService = typeService;
        _sourceDialectName = sourceDialectName;

        if (!(connection is NpgsqlConnection npgsqlConnection))
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        var setupCommand = npgsqlConnection.CreateCommand();
        setupCommand.CommandText = @"
                DROP SCHEMA public CASCADE;
                CREATE SCHEMA public;
                CREATE EXTENSION IF NOT EXISTS ""pgcrypto"";";
        await setupCommand.ExecuteNonQueryAsync();

        foreach (var table in _tables)
        {
            string createTableScript = BuildCreateTableScript(table, _typeService, _sourceDialectName);
            try
            {
                var command = npgsqlConnection.CreateCommand();
                command.CommandText = createTableScript;
                await command.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException ex)
            {
                throw new Exception($"Failed to create table '{table.TableName}'.\n{createTableScript}", ex);
            }

            foreach (var trigger in table.Triggers)
            {
                string createTriggerScript = BuildCreateTriggerScript(trigger);
                try
                {
                    var triggerCommand = npgsqlConnection.CreateCommand();
                    triggerCommand.CommandText = createTriggerScript;
                    await triggerCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    // This is not a fatal error, so we can just log it.
                    System.Diagnostics.Debug.WriteLine($"Warning: Could not create trigger placeholder for '{trigger.Name}': {ex.Message}");
                }
            }
        }
    }

    private string BuildCreateTableScript(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        var allDefinitions = new List<string>();

        sb.AppendLine($"CREATE TABLE \"{ts.TableName}\" (");

        foreach (var col in ts.Columns)
        {
            var columnLine = new StringBuilder();
            var dbColInfo = new DbColumnInfo() { TypeName = col.ColumnType, Length = col.Length, Precision = col.Precision, Scale = col.Scale };
            string targetType = typeService.TranslateType(dbColInfo, sourceDialectName, "PostgreSql") ?? "TEXT";

            if (col.IsIdentity)
            {
                if (targetType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)) targetType = "SERIAL";
                else if (targetType.Equals("BIGINT", StringComparison.OrdinalIgnoreCase)) targetType = "BIGSERIAL";
            }

            columnLine.Append($"    \"{col.ColumnName}\" {targetType}");

            if (!col.IsNullable)
            {
                columnLine.Append(" NOT NULL");
            }

            // USE THE GENERATOR to create the default value from the AST
            if (col.DefaultValueAst != null && !col.IsIdentity)
            {
                try
                {
                    string defaultValueSql = GenerateSqlForNode(col.DefaultValueAst);
                    // Fix boolean defaults: PostgreSQL expects 'true'/'false', not '1'/'0'
                    if (targetType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase))
                    {
                        if (defaultValueSql == "1") defaultValueSql = "true";
                        else if (defaultValueSql == "0") defaultValueSql = "false";
                    }
                    columnLine.Append($" DEFAULT {defaultValueSql}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not generate SQL for default value of column {col.ColumnName} in table {ts.TableName}: {ex.Message}");
                }
            }
            allDefinitions.Add(columnLine.ToString());
        }

        if (ts.PrimaryKey.Any())
        {
            var pkCols = string.Join(", ", ts.PrimaryKey.Select(c => $"\"{c}\""));
            string pkName = $"PK_{ts.TableName}";
            allDefinitions.Add($"    CONSTRAINT \"{pkName}\" PRIMARY KEY ({pkCols})");
        }

        sb.Append(string.Join(",\n", allDefinitions));
        sb.AppendLine("\n);");
        return sb.ToString();
    }

    public async Task WriteConstraintsAndIndexesAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        if (_tables is null)
        {
            throw new InvalidOperationException("WriteSchemaAsync must be called first.");
        }
        if (!(connection is NpgsqlConnection npgsqlConnection))
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        foreach (var table in _tables)
        {
            foreach (var uq in table.UniqueConstraints)
            {
                string addUqScript = BuildAddUniqueConstraintScript(table.TableName, uq);
                try
                {
                    var uqCommand = npgsqlConnection.CreateCommand();
                    uqCommand.CommandText = addUqScript;
                    await uqCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    await progressHandler.SendWarningAsync($"Could not create unique constraint '{uq.ConstraintName}' on '{table.TableName}': {ex.Message}");
                }
            }

            foreach (var fk in table.ForeignKeys)
            {
                string addFkScript = BuildAddForeignKeyConstraintScript(table.TableName, fk);
                try
                {
                    var fkCommand = npgsqlConnection.CreateCommand();
                    fkCommand.CommandText = addFkScript;
                    await fkCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    await progressHandler.SendWarningAsync($"Could not create foreign key '{fk.ForeignKeyName}' on '{table.TableName}': {ex.Message}");
                }
            }

            foreach (var chk in table.CheckConstraints)
            {
                string addChkScript = BuildAddCheckConstraintScript(table.TableName, chk);
                try
                {
                    var chkCommand = npgsqlConnection.CreateCommand();
                    chkCommand.CommandText = addChkScript;
                    await chkCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    await progressHandler.SendWarningAsync($"Could not create check constraint '{chk.ConstraintName}' on '{table.TableName}': {ex.Message}");
                }
            }

            foreach (var index in table.Indexes)
            {
                // Skip indexes on XML columns as PostgreSQL does not support btree indexes on XML
                bool hasXmlColumn = index.Columns.Any(ic => table.Columns.Any(c => c.ColumnName == ic.ColumnName && c.ColumnType.ToLower().Contains("xml")));
                if (hasXmlColumn)
                {
                    await progressHandler.SendWarningAsync($"Skipping index '{index.IndexName}' on '{table.TableName}' as it includes XML columns, which are not supported in PostgreSQL btree indexes.");
                    continue;
                }

                string createIndexScript = BuildCreateIndexScript(table.TableName, index);
                try
                {
                    var indexCommand = npgsqlConnection.CreateCommand();
                    indexCommand.CommandText = createIndexScript;
                    await indexCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    if (ex.Message.Contains("exceeds maximum"))
                    {
                        // Try creating a hash index instead for large keys
                        string hashIndexScript = BuildCreateHashIndexScript(table.TableName, index);
                        try
                        {
                            var hashIndexCommand = npgsqlConnection.CreateCommand();
                            hashIndexCommand.CommandText = hashIndexScript;
                            await hashIndexCommand.ExecuteNonQueryAsync();
                            await progressHandler.SendWarningAsync($"Created hash index '{index.IndexName}' on '{table.TableName}' instead of btree due to key size exceeding limit.");
                        }
                        catch (NpgsqlException hashEx)
                        {
                            await progressHandler.SendWarningAsync($"Could not create hash index '{index.IndexName}' on '{table.TableName}': {hashEx.Message}");
                        }
                    }
                    else
                    {
                        await progressHandler.SendWarningAsync($"Could not create index '{index.IndexName}' on '{table.TableName}': {ex.Message}");
                    }
                }
            }
        }
    }

    private string BuildAddCheckConstraintScript(string tableName, CheckConstraintSchema chk)
    {
        // Preprocess the check expression to replace SQL Server brackets with PostgreSQL quotes
        string processedCheckExpression = chk.CheckClauseAsString.Replace("[", "\"").Replace("]", "\"");

        if (chk.CheckClauseAst != null)
        {
            try
            {
                // Try to use the AST-generated expression
                string translatedExpression = GenerateSqlForNode(chk.CheckClauseAst);
                // If the translated expression still contains brackets, fall back to processed
                if (translatedExpression.Contains("[") || translatedExpression.Contains("]"))
                {
                    return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({processedCheckExpression});";
                }
                return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({translatedExpression});";
            }
            catch
            {
                // Fall back to processed expression if AST generation fails
                return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({processedCheckExpression});";
            }
        }
        else
        {
            // Use the processed raw expression
            return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({processedCheckExpression});";
        }
    }

    /// <summary>
    /// This is the "Generator". It walks the universal AST and generates
    /// PostgreSQL-specific SQL syntax.
    /// </summary>
    private string GenerateSqlForNode(ExpressionNode node)
    {
        return node switch
        {
            FunctionCallNode func => func.UniversalFunctionName switch
            {
                "GetCurrentTimestamp" => "NOW()",
                "GenerateUuid" => "gen_random_uuid()",
                "upper" => $"UPPER({GenerateSqlForNode(func.Arguments[0])})",
                "dateadd" => GenerateDateAddSql(func),
                _ => throw new NotSupportedException($"Unsupported universal function: {func.UniversalFunctionName}")
            },
            LiteralNode literal => literal.Value switch
            {
                string s when IsExpressionString(s) => s,
                string s => $"'{s.Replace("'", "''")}'",
                bool b => b ? "true" : "false",
                null => "NULL",
                _ => literal.Value?.ToString() ?? "NULL"
            },
            IdentifierNode identifier => $"\"{identifier.Name.Trim('[', ']')}\"", // PostgreSQL uses double quotes, strip SQL Server brackets
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
            return $"({GenerateSqlForNode(op.Left!)} ILIKE {GenerateSqlForNode(op.Right!)})";
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
        string interval = $"{(number >= 0 ? "+" : "")}{Math.Abs(number)} {MapDatePart(part)}";
        return $"({dateSql} + INTERVAL '{interval}')";
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

    // --- NO CHANGES to the methods below this point ---

    public async Task WriteViewsAsync(DbConnection connection, List<ViewSchema> views)
    {
        if (!(connection is NpgsqlConnection npgsqlConnection))
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        foreach (var view in views)
        {
            string createViewScript = BuildCreateViewScript(view);
            try
            {
                var viewCommand = npgsqlConnection.CreateCommand();
                viewCommand.CommandText = createViewScript;
                await viewCommand.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException ex)
            {
                throw new Exception($"Failed to create view '{view.ViewName}'.\n{createViewScript}", ex);
            }
        }
    }

    private string BuildCreateViewScript(ViewSchema view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE OR REPLACE VIEW \"{view.ViewName}\" AS");
        sb.AppendLine("SELECT 'This is a placeholder view. The original T-SQL body is below for manual translation.' AS MigrationMessage;");
        sb.AppendLine("\n/* --- ORIGINAL VIEW DEFINITION ---");
        sb.AppendLine(view.ViewSQL.Trim());
        sb.AppendLine("*/");
        return sb.ToString();
    }

    private string BuildCreateIndexScript(string tableName, IndexSchema index)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique)
        {
            sb.Append("UNIQUE ");
        }
        sb.Append($"INDEX \"{index.IndexName}\" ON \"{tableName}\" (");
        sb.Append(string.Join(", ", index.Columns.Select(c => $"\"{c.ColumnName}\"" + (c.IsAscending ? " ASC" : " DESC"))));
        sb.Append(");");
        return sb.ToString();
    }

    private string BuildCreateHashIndexScript(string tableName, IndexSchema index)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique)
        {
            sb.Append("UNIQUE ");
        }
        sb.Append($"INDEX \"{index.IndexName}\" ON \"{tableName}\" USING HASH (");
        // Hash indexes don't support ASC/DESC, and typically for single columns, but allow multiple
        sb.Append(string.Join(", ", index.Columns.Select(c => $"\"{c.ColumnName}\"")));
        sb.Append(");");
        return sb.ToString();
    }

    private string BuildCreateTriggerScript(TriggerSchema trigger)
    {
        // This is a simplified placeholder implementation
        var sb = new StringBuilder();
        string functionName = $"placeholder_trigger_function_{trigger.Name}";

        sb.AppendLine($"CREATE OR REPLACE FUNCTION {functionName}() RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN RETURN NEW; END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();
        sb.AppendLine($"CREATE TRIGGER \"{trigger.Name}\"");
        sb.Append($" AFTER {trigger.Event.ToString().ToUpper()} ON \"{trigger.Table}\"");
        sb.AppendLine(" FOR EACH ROW");
        sb.AppendLine($"EXECUTE PROCEDURE {functionName}();");

        return sb.ToString();
    }

    private string BuildAddForeignKeyConstraintScript(string tableName, ForeignKeySchema fk)
    {
        var localColumns = string.Join(", ", fk.LocalColumns.Select(c => $"\"{c}\""));
        var foreignColumns = string.Join(", ", fk.ForeignColumns.Select(c => $"\"{c}\""));
        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{fk.ForeignKeyName}\" ");
        sb.Append($"FOREIGN KEY ({localColumns}) REFERENCES \"{fk.ForeignTable}\" ({foreignColumns})");
        if (!string.IsNullOrEmpty(fk.DeleteAction) && !fk.DeleteAction.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ON DELETE {fk.DeleteAction}");
        }
        if (!string.IsNullOrEmpty(fk.UpdateAction) && !fk.UpdateAction.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ON UPDATE {fk.UpdateAction}");
        }
        sb.Append(";");
        return sb.ToString();
    }

    private string BuildAddUniqueConstraintScript(string tableName, UniqueConstraintSchema uq)
    {
        var uniqueColumns = string.Join(", ", uq.Columns.Select(c => $"\"{c}\""));
        return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{uq.ConstraintName}\" UNIQUE ({uniqueColumns});";
    }
}