using System.Data.Common;
using System.Text;
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
            var createTableScript = BuildCreateTableScript(table, typeService, sourceDialectName);
            var command = new SqliteCommand(createTableScript, sqliteConnection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private string BuildCreateTableScript(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        var constraints = new List<string>();

        sb.AppendLine($"CREATE TABLE [{ts.TableName}] (");

        // 1. Define Columns
        for (int i = 0; i < ts.Columns.Count; i++)
        {
            var col = ts.Columns[i];

            // ==========================================================
            //  FIX: Populate the DbColumnInfo object correctly
            // ==========================================================
            var sourceColumnInfo = new DbColumnInfo
            {
                TypeName = col.ColumnType,
                Length = col.Length,
                Precision = col.Precision,
                Scale = col.Scale
            };

            string targetType = typeService.TranslateType(sourceColumnInfo, sourceDialectName, "SQLite") ?? "TEXT";

            var columnLine = new StringBuilder($"    [{col.ColumnName}] {targetType}");
            if (!col.IsNullable)
            {
                columnLine.Append(" NOT NULL");
            }
            // (Default value logic can be expanded here)

            sb.AppendLine(columnLine.ToString() + (i < ts.Columns.Count - 1 ? "," : ""));
        }

        // 2. Define Primary Key Constraint
        if (ts.PrimaryKey.Any())
        {
            var pkColumns = string.Join(", ", ts.PrimaryKey.Select(pk => $"[{pk}]"));
            constraints.Add($"    PRIMARY KEY ({pkColumns})");
        }

        // 3. Define Foreign Key Constraints
        if (ts.ForeignKeys.Any())
        {
            foreach (var fk in ts.ForeignKeys)
            {
                constraints.Add($"    FOREIGN KEY ([{fk.ColumnName}]) REFERENCES [{fk.ForeignTableName}]([{fk.ForeignColumnName}])");
            }
        }

        // 4. Append all constraints to the CREATE TABLE statement
        if (constraints.Any())
        {
            sb.AppendLine(",");
            sb.AppendLine(string.Join(",\n", constraints));
        }

        sb.AppendLine(");");

        // 5. Append CREATE INDEX statements
        if (ts.Indexes.Any())
        {
            foreach (var index in ts.Indexes)
            {
                string unique = index.IsUnique ? "UNIQUE" : "";
                var indexColumns = string.Join(", ", index.Columns.Select(c => $"[{c.ColumnName}]" + (c.IsAscending ? "" : " DESC")));

                var safeIndexName = $"IX_{ts.TableName}_{string.Join("_", index.Columns.Select(c => c.ColumnName))}";

                sb.AppendLine($"CREATE {unique} INDEX IF NOT EXISTS [{safeIndexName}] ON [{ts.TableName}] ({indexColumns});");
            }
        }

        return sb.ToString();
    }
}
