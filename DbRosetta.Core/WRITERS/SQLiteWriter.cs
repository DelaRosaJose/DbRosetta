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
            var createTableSql = BuildCreateTableQuery(table, typeService, sourceDialectName);
            var command = new SqliteCommand(createTableSql, sqliteConnection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private string BuildCreateTableQuery(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{ts.TableName}] (");

        for (int i = 0; i < ts.Columns.Count; i++)
        {
            var col = ts.Columns[i];
            var sourceColumnInfo = new DbColumnInfo
            {
                TypeName = col.ColumnType,
                Length = col.Length,
                Precision = col.Precision,
                Scale = col.Scale
            };

            // Use the type service to translate the column type to SQLite!
            string targetType = typeService.TranslateType(sourceColumnInfo, sourceDialectName, "SQLite") ?? "TEXT";

            sb.Append($"    [{col.ColumnName}] {targetType}");

            if (!col.IsNullable)
            {
                sb.Append(" NOT NULL");
            }

            // Simplified default value handling for now
            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                // Basic check to see if default is numeric or needs quotes
                if (double.TryParse(col.DefaultValue, out _))
                {
                    sb.Append($" DEFAULT {col.DefaultValue}");
                }
                else
                {
                    sb.Append($" DEFAULT '{col.DefaultValue.Replace("'", "''")}'");
                }
            }

            sb.AppendLine(i < ts.Columns.Count - 1 || ts.PrimaryKey.Any() ? "," : "");
        }

        // Add primary keys
        if (ts.PrimaryKey.Any())
        {
            var pkColumns = string.Join(", ", ts.PrimaryKey.Select(pk => $"[{pk}]"));
            sb.AppendLine($"    PRIMARY KEY ({pkColumns})");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }
}
