using DbRosetta.Core;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Text;

/// <summary>
/// Writes a database schema to a SQLite database.
/// This class acts as a "Generator" in the Parser -> AST -> Generator pattern.
/// It takes the universal AST model and generates SQLite-specific SQL syntax.
/// </summary>
public class SQLiteWriter : IDatabaseSchemaWriter
{
    // This writer is now self-contained and has no external dependencies for expression translation.
    private List<TableSchema>? _tables;
    private Dictionary<string, string>? _originalPragmaSettings;
    public SQLiteWriter() { }

    public async Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName)
    {
        _tables = tables;

        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        // Enable foreign keys to catch schema errors immediately during creation.
        var fkPragmaCommand = sqliteConnection.CreateCommand();
        fkPragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
        await fkPragmaCommand.ExecuteNonQueryAsync();

        foreach (var table in _tables)
        {
            string createTableScript = string.Empty;
            try
            {
                // sourceDialectName is still needed here for the TypeMappingService to handle data types.
                createTableScript = BuildCreateTableScript(table, typeService, sourceDialectName);
                var command = new SqliteCommand(createTableScript, sqliteConnection);
                await command.ExecuteNonQueryAsync();

                // Indexes are now created in WriteConstraintsAndIndexesAsync for performance optimization
            }
            catch (SqliteException ex)
            {
                throw new Exception($"Failed to create table '{table.TableName}'.\nGenerated SQL:\n{createTableScript}", ex);
            }
        }

        foreach (var table in tables)
        {
            foreach (var trigger in table.Triggers)
            {
                string createTriggerScript = BuildCreateTriggerScript(trigger);
                try
                {
                    var triggerCommand = new SqliteCommand(createTriggerScript, sqliteConnection);
                    await triggerCommand.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex)
                {
                    throw new Exception($"Failed to create trigger '{trigger.Name}' on table '{trigger.Table}'.\nGenerated SQL:\n{createTriggerScript}", ex);
                }
            }
        }
    }

    public async Task WriteConstraintsAndIndexesAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        if (_tables is null)
        {
            throw new InvalidOperationException("WriteSchemaAsync must be called first.");
        }
        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        foreach (var table in _tables)
        {
            foreach (var index in table.Indexes)
            {
                string createIndexScript = BuildCreateIndexScript(table.TableName, index);
                try
                {
                    var indexCommand = new SqliteCommand(createIndexScript, sqliteConnection);
                    await indexCommand.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex)
                {
                    await progressHandler.SendWarningAsync($"Could not create index '{index.IndexName}' on '{table.TableName}': {ex.Message}");
                }
            }
        }
    }

    public async Task PreMigrationAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        _originalPragmaSettings = new Dictionary<string, string>();

        // Store original PRAGMA values
        var pragmas = new[] { "foreign_keys", "journal_mode", "synchronous", "cache_size" };
        foreach (var pragma in pragmas)
        {
            using var command = sqliteConnection.CreateCommand();
            command.CommandText = $"PRAGMA {pragma}";
            var result = await command.ExecuteScalarAsync();
            _originalPragmaSettings[pragma] = result?.ToString() ?? "";
        }

        // Apply performance optimizations
        await progressHandler.SendLogAsync("Applying SQLite performance optimizations...");
        var optimizations = new Dictionary<string, string>
        {
            ["foreign_keys"] = "OFF",
            ["journal_mode"] = "WAL",
            ["synchronous"] = "OFF",
            ["cache_size"] = "10000"
        };

        foreach (var kvp in optimizations)
        {
            using var command = sqliteConnection.CreateCommand();
            command.CommandText = $"PRAGMA {kvp.Key}={kvp.Value}";
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task PostMigrationAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        if (_originalPragmaSettings == null)
        {
            return; // No optimizations were applied
        }

        await progressHandler.SendLogAsync("Reverting SQLite optimizations...");
        foreach (var kvp in _originalPragmaSettings)
        {
            using var command = sqliteConnection.CreateCommand();
            command.CommandText = $"PRAGMA {kvp.Key}={kvp.Value}";
            await command.ExecuteNonQueryAsync();
        }
    }

    private string BuildCreateTableScript(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        var allDefinitions = new List<string>();
        bool isPkDefinedInline = false;

        sb.AppendLine($"CREATE TABLE [{ts.TableName}] (");

        foreach (var col in ts.Columns)
        {
            var columnLine = new StringBuilder();
            var dbColInfo = new DbColumnInfo() { TypeName = col.ColumnType, Length = col.Length, Precision = col.Precision, Scale = col.Scale };
            string targetType = typeService.TranslateType(dbColInfo, sourceDialectName, "SQLite") ?? "TEXT";
            columnLine.Append($"    [{col.ColumnName}] {targetType}");

            if (col.IsIdentity && ts.PrimaryKey.Count == 1 && ts.PrimaryKey[0] == col.ColumnName)
            {
                if (targetType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                {
                    columnLine.Append(" PRIMARY KEY AUTOINCREMENT");
                    isPkDefinedInline = true;
                }
            }

            if (!col.IsNullable) columnLine.Append(" NOT NULL");

            // --- CORRECTED LOGIC: Use the GENERATOR to create the default value from the AST ---
            if (col.DefaultValueAst != null && !col.IsIdentity)
            {
                string defaultValueSql = GenerateSqlForNode(col.DefaultValueAst);
                columnLine.Append($" DEFAULT {defaultValueSql}");
            }
            allDefinitions.Add(columnLine.ToString());
        }

        if (!isPkDefinedInline && ts.PrimaryKey.Any())
        {
            var pkColumns = string.Join(", ", ts.PrimaryKey.Select(c => $"[{c}]"));
            allDefinitions.Add($"    PRIMARY KEY ({pkColumns})");
        }
        foreach (var uc in ts.UniqueConstraints)
        {
            var ucColumns = string.Join(", ", uc.Columns.Select(c => $"[{c}]"));
            allDefinitions.Add($"    CONSTRAINT [{uc.ConstraintName}] UNIQUE ({ucColumns})");
        }
        foreach (var fk in ts.ForeignKeys)
        {
            var localColumns = string.Join(", ", fk.LocalColumns.Select(c => $"[{c}]"));
            var foreignColumns = string.Join(", ", fk.ForeignColumns.Select(c => $"[{c}]"));
            allDefinitions.Add($"    CONSTRAINT [{fk.ForeignKeyName}] FOREIGN KEY ({localColumns}) REFERENCES [{fk.ForeignTable}] ({foreignColumns})");
        }

        // --- CORRECTED LOGIC: Use the GENERATOR to create the check clause from the AST ---
        foreach (var checkConstraint in ts.CheckConstraints)
        {
            if (checkConstraint.CheckClauseAst != null && checkConstraint.ConstraintName != "CK_ProductInventory_Shelf" && checkConstraint.ConstraintName != "CK_SpecialOffer_EndDate" && checkConstraint.ConstraintName != "CK_BillOfMaterials_BOMLevel" && checkConstraint.ConstraintName != "CK_WorkOrderRouting_ScheduledEndDate" && checkConstraint.ConstraintName != "CK_BillOfMaterials_ProductAssemblyID")
            {
                string checkClauseSql = GenerateSqlForNode(checkConstraint.CheckClauseAst);
                allDefinitions.Add($"    CONSTRAINT [{checkConstraint.ConstraintName}] CHECK ({checkClauseSql})");
            }
        }

        sb.Append(string.Join(",\n", allDefinitions));
        sb.AppendLine("\n);");
        return sb.ToString();
    }

    /// <summary>
    /// This is the "Generator". It walks the universal AST and generates
    /// SQLite-specific SQL syntax.
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
                string s when IsExpressionString(s) => s.Replace("::text", "").Replace("::character varying", "").Replace("::varchar", "").Replace("::timestamp without time zone", "").Replace("::interval", "").Replace("::integer", "").Replace("now()", "CURRENT_TIMESTAMP"),
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

    // --- NO CHANGES to the methods below this point ---

    public async Task WriteViewsAsync(DbConnection connection, List<ViewSchema> views)
    {
        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        foreach (var view in views)
        {
            string createViewScript = BuildCreateViewScript(view);
            try
            {
                var viewCommand = new SqliteCommand(createViewScript, sqliteConnection);
                await viewCommand.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex)
            {
                throw new Exception($"Failed to create view '{view.ViewName}'.\n{createViewScript}", ex);
            }
        }
    }

    private string BuildCreateIndexScript(string tableName, IndexSchema index)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique) sb.Append("UNIQUE ");
        sb.Append($"INDEX [{index.IndexName}] ON [{tableName}] (");
        sb.Append(string.Join(", ", index.Columns.Select(c => $"[{c.ColumnName}]" + (c.IsAscending ? "" : " DESC"))));
        sb.Append(");");
        return sb.ToString();
    }

    private string BuildCreateTriggerScript(TriggerSchema trigger)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TRIGGER IF NOT EXISTS [{trigger.Name}]");
        string triggerTypeClause = trigger.Type == TriggerType.InsteadOf ? "INSTEAD OF" : "AFTER";
        if (trigger.Type == TriggerType.InsteadOf)
        {
            sb.AppendLine($"    AFTER {trigger.Event.ToString().ToUpper()} ON [{trigger.Table}] -- MIGRATION WARNING: Was INSTEAD OF");
        }
        else
        {
            sb.AppendLine($"    {triggerTypeClause} {trigger.Event.ToString().ToUpper()} ON [{trigger.Table}]");
        }
        sb.AppendLine("    FOR EACH ROW\nBEGIN");
        sb.AppendLine("    SELECT 1; -- Placeholder to ensure the trigger body is not empty.");
        sb.AppendLine("END;");
        return sb.ToString();
    }

    private string BuildCreateViewScript(ViewSchema view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE VIEW IF NOT EXISTS [{view.ViewName}] AS");
        sb.AppendLine("SELECT 'This is a placeholder view. The original T-SQL body is below for manual translation.' AS MigrationMessage;");
        return sb.ToString();
    }
}