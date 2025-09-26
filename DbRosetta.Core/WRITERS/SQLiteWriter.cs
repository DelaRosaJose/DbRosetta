using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

public class SQLiteWriter : IDatabaseWriter
{
    public async Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName)
    {
        if (!(connection is SqliteConnection sqliteConnection))
        {
            throw new ArgumentException("A SqliteConnection is required.", nameof(connection));
        }

        foreach (var table in tables)
        {
            string createTableScript = string.Empty;
            try
            {
                createTableScript = BuildCreateTableScript(table, typeService, sourceDialectName);
                var command = new SqliteCommand(createTableScript, sqliteConnection);
                await command.ExecuteNonQueryAsync();

                foreach (var index in table.Indexes)
                {
                    var createIndexScript = BuildCreateIndexScript(table.TableName, index);
                    var indexCommand = new SqliteCommand(createIndexScript, sqliteConnection);
                    await indexCommand.ExecuteNonQueryAsync();
                }
            }
            catch (SqliteException ex)
            {
                throw new Exception($"Failed to create table '{table.TableName}'.\n{createTableScript}", ex);
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
                    throw new Exception($"Failed to create trigger '{trigger.Name}' on table '{trigger.Table}'.\n{createTriggerScript}", ex);
                }
            }
        }
    }

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
            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                string defaultValue = TranslateSqlServerExpressionToSQLite(col.DefaultValue, true);
                columnLine.Append($" DEFAULT {defaultValue}");
            }
            allDefinitions.Add(columnLine.ToString());
        }

        if (!isPkDefinedInline && ts.PrimaryKey.Any())
        {
            allDefinitions.Add($"    PRIMARY KEY ({string.Join(", ", ts.PrimaryKey.Select(c => $"[{c}]"))})");
        }
        foreach (var uc in ts.UniqueConstraints)
        {
            allDefinitions.Add($"    UNIQUE ({string.Join(", ", uc.Columns.Select(c => $"[{c}]"))})");
        }
        foreach (var fk in ts.ForeignKeys)
        {
            // Join the column lists with commas and quote them
            var localColumns = string.Join(", ", fk.LocalColumns.Select(c => $"[{c}]"));
            var foreignColumns = string.Join(", ", fk.ForeignColumns.Select(c => $"[{c}]"));

            // Build the full constraint definition
            allDefinitions.Add($"    CONSTRAINT [{fk.ForeignKeyName}] FOREIGN KEY ({localColumns}) REFERENCES [{fk.ForeignTable}] ({foreignColumns})");
        }

        // Add Check Constraint definitions
        if (ts.CheckConstraints.Any())
        {
            // Loop through the CheckConstraintSchema objects
            foreach (var checkConstraint in ts.CheckConstraints)
            {
                // Use the .CheckClause property of the object
                string translatedClause = TranslateSqlServerExpressionToSQLite(checkConstraint.CheckClause, false);
                allDefinitions.Add($"    CONSTRAINT [{checkConstraint.ConstraintName}] CHECK ({translatedClause})");
            }
        }

        sb.Append(string.Join(",\n", allDefinitions));
        sb.AppendLine("\n);");
        return sb.ToString();
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
        sb.AppendLine();
        if (trigger.Type == TriggerType.InsteadOf)
        {
            sb.AppendLine("    -- # MIGRATION WARNING: SQLite only supports INSTEAD OF triggers on VIEWs.");
        }
        else
        {
            sb.AppendLine("    -- The original T-SQL body below must be manually translated to SQLite syntax.");
        }
        sb.AppendLine("    -- --- ORIGINAL SQL SERVER TRIGGER ---");
        var bodyLines = trigger.Body.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in bodyLines)
        {
            sb.AppendLine($"    -- {line}");
        }
        sb.AppendLine("END;");
        return sb.ToString();
    }

    private string BuildCreateViewScript(ViewSchema view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE VIEW IF NOT EXISTS [{view.ViewName}] AS");
        sb.AppendLine("SELECT 'This is a placeholder view. The original T-SQL body is below for manual translation.' AS MigrationMessage;");
        sb.AppendLine();
        sb.AppendLine("-- --- ORIGINAL SQL SERVER VIEW DEFINITION ---");
        var bodyLines = view.ViewSQL.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in bodyLines)
        {
            sb.AppendLine($"-- {line}");
        }
        return sb.ToString();
    }

    private string TranslateSqlServerExpressionToSQLite(string expression, bool isDefaultConstraint)
    {
        if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
        string workExpression = expression.Trim();

        if (isDefaultConstraint)
        {
            Match match = Regex.Match(workExpression, @"^\(\((-?\d+(\.\d+)?)\)\)$"); if (match.Success) return match.Groups[1].Value;
            match = Regex.Match(workExpression, @"^\(N?'(.*)'\)$"); if (match.Success) return $"'{match.Groups[1].Value.Replace("'", "''")}'";
            if (Regex.IsMatch(workExpression, @"^\((getdate|sysdatetime)\(\)\)$", RegexOptions.IgnoreCase)) return "CURRENT_TIMESTAMP";
            if (Regex.IsMatch(workExpression, @"^\(newid\(\)\)$", RegexOptions.IgnoreCase)) return "(lower(hex(randomblob(16))))";
            match = Regex.Match(workExpression, @"^\(CONVERT\s*\(\s*\[?bit\]?\s*,\s*\(\s*([01])\s*\)\s*\)\)$", RegexOptions.IgnoreCase); if (match.Success) return match.Groups[1].Value;
        }

        string translated = workExpression;
        translated = Regex.Replace(translated, @"\bGETDATE\(\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bSYSDATETIME\(\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*year\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 years')", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*month\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 months')", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*day\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 days')", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"(\[\w+\])\s+LIKE\s*('\[.*\]')", "UPPER($1) GLOB $2", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"(\[\w+\])\s+LIKE", "UPPER($1) LIKE", RegexOptions.IgnoreCase);

        if (!isDefaultConstraint && translated.StartsWith("(") && translated.EndsWith(")"))
        {
            translated = translated.Substring(1, translated.Length - 2);
        }

        return translated;
    }
}
