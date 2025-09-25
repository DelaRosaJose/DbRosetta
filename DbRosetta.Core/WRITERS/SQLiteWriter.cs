using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

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
                throw new Exception($"Failed to create table '{table.TableName}'. Review the generated script for syntax errors.\n--- SCRIPT START ---\n{createTableScript}\n--- SCRIPT END ---", ex);
            }
        }
    }

    public async Task WriteDataAsync(DbConnection sourceConnection, DbConnection destinationConnection, List<TableSchema> tables)
    {
        await Task.CompletedTask;
    }

    private string BuildCreateTableScript(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        var allDefinitions = new List<string>();

        sb.AppendLine($"CREATE TABLE [{ts.TableName}] (");

        foreach (var col in ts.Columns)
        {
            var columnLine = new StringBuilder();
            var dbColInfo = new DbColumnInfo() { TypeName = col.ColumnType, Length = col.Length, Precision = col.Precision, Scale = col.Scale };
            string targetType = typeService.TranslateType(dbColInfo, sourceDialectName, "SQLite") ?? "TEXT";
            columnLine.Append($"    [{col.ColumnName}] {targetType}");
            if (!col.IsNullable) columnLine.Append(" NOT NULL");
            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                string defaultValue = TranslateSqlServerExpressionToSQLite(col.DefaultValue, isDefaultConstraint: true);
                columnLine.Append($" DEFAULT {defaultValue}");
            }
            allDefinitions.Add(columnLine.ToString());
        }

        if (ts.PrimaryKey.Any())
        {
            var pkCols = string.Join(", ", ts.PrimaryKey.Select(c => $"[{c}]"));
            allDefinitions.Add($"    PRIMARY KEY ({pkCols})");
        }

        foreach (var fk in ts.ForeignKeys)
        {
            allDefinitions.Add($"    FOREIGN KEY ([{fk.ColumnName}]) REFERENCES [{fk.ForeignTableName}] ([{fk.ForeignColumnName}])");
        }

        if (ts.CheckConstraints.Any())
        {
            foreach (var checkClause in ts.CheckConstraints)
            {
                string translatedClause = TranslateSqlServerExpressionToSQLite(checkClause, isDefaultConstraint: false);
                allDefinitions.Add($"    CHECK ({translatedClause})");
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
        var indexCols = index.Columns.Select(c => $"[{c.ColumnName}]" + (c.IsAscending ? "" : " DESC"));
        sb.Append(string.Join(", ", indexCols));
        sb.Append(");");
        return sb.ToString();
    }

    private string TranslateSqlServerExpressionToSQLite(string expression, bool isDefaultConstraint)
    {
        if (string.IsNullOrWhiteSpace(expression)) return string.Empty;

        string workExpression = expression.Trim();

        if (isDefaultConstraint)
        {
            Match match = Regex.Match(workExpression, @"^\(\((-?\d+(\.\d+)?)\)\)$");
            if (match.Success) return match.Groups[1].Value;

            match = Regex.Match(workExpression, @"^\(N?'(.*)'\)$");
            if (match.Success) return $"'{match.Groups[1].Value.Replace("'", "''")}'";

            if (Regex.IsMatch(workExpression, @"^\((getdate|sysdatetime)\(\)\)$", RegexOptions.IgnoreCase)) return "CURRENT_TIMESTAMP";
            if (Regex.IsMatch(workExpression, @"^\(newid\(\)\)$", RegexOptions.IgnoreCase)) return "(lower(hex(randomblob(16))))";

            match = Regex.Match(workExpression, @"^\(CONVERT\s*\(\s*\[?bit\]?\s*,\s*\(\s*([01])\s*\)\s*\)\)$", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }

        string translated = workExpression;
        translated = Regex.Replace(translated, @"\bGETDATE\(\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bSYSDATETIME\(\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bYEAR\((.*?)\)", "strftime('%Y', $1)", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bMONTH\((.*?)\)", "strftime('%m', $1)", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bDAY\((.*?)\)", "strftime('%d', $1)", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*year\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 years')", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*month\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 months')", RegexOptions.IgnoreCase);
        translated = Regex.Replace(translated, @"\bdateadd\s*\(\s*day\s*,\s*\(?(-?\d+)\)?\s*,\s*([^)]+)\)", "date($2, '$1 days')", RegexOptions.IgnoreCase);

        // --- FINAL FIX: Translate SQL Server LIKE with character sets to SQLite GLOB and make it case-insensitive ---
        // This pattern finds a column [ColName] followed by LIKE '[...]' and converts it to UPPER([ColName]) GLOB '[...]'
        translated = Regex.Replace(translated, @"(\[\w+\])\s+LIKE\s*('\[.*\]')", "UPPER($1) GLOB $2", RegexOptions.IgnoreCase);

        // This handles any remaining simple LIKEs (not using '[...]') to be case-insensitive
        translated = Regex.Replace(translated, @"(\[\w+\])\s+LIKE", "UPPER($1) LIKE", RegexOptions.IgnoreCase);


        if (!isDefaultConstraint && translated.StartsWith("(") && translated.EndsWith(")"))
        {
            translated = translated.Substring(1, translated.Length - 2);
        }

        return translated;
    }
}
