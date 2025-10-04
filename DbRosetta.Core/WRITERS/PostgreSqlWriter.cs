
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
                string defaultValueSql = GenerateSqlForNode(col.DefaultValueAst);
                columnLine.Append($" DEFAULT {defaultValueSql}");
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
                string createIndexScript = BuildCreateIndexScript(table.TableName, index);
                try
                {
                    var indexCommand = npgsqlConnection.CreateCommand();
                    indexCommand.CommandText = createIndexScript;
                    await indexCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    await progressHandler.SendWarningAsync($"Could not create index '{index.IndexName}' on '{table.TableName}': {ex.Message}");
                }
            }
        }
    }

    private string BuildAddCheckConstraintScript(string tableName, CheckConstraintSchema chk)
    {
        if (chk.CheckClauseAst == null)
        {
            // Cannot create a constraint without a definition.
            return string.Empty;
        }
        // USE THE GENERATOR to create the check clause from the AST
        string translatedExpression = GenerateSqlForNode(chk.CheckClauseAst);
        return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({translatedExpression});";
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
                _ => throw new NotSupportedException($"Unsupported universal function: {func.UniversalFunctionName}")
            },
            LiteralNode literal => literal.Value switch
            {
                string s => $"'{s.Replace("'", "''")}'",
                bool b => b ? "true" : "false",
                null => "NULL",
                // Handle raw string fallbacks from a simple parser
                var val when val is string => val.ToString()!,
                _ => literal.Value?.ToString() ?? "NULL"
            },
            IdentifierNode identifier => $"\"{identifier.Name}\"", // PostgreSQL uses double quotes
            OperatorNode op => $"({GenerateSqlForNode(op.Left!)} {op.Operator} {GenerateSqlForNode(op.Right!)})",
            _ => throw new NotSupportedException($"Unsupported ExpressionNode type: {node.GetType().Name}")
        };
    }

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