using Npgsql;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;


public class PostgreSqlWriter : IDatabaseWriter
{
    public async Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName)
    {
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

        foreach (var table in tables)
        {
            string createTableScript = BuildCreateTableScript(table, typeService, sourceDialectName);
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
        }
    }

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
        sb.AppendLine("\n/* --- ORIGINAL SQL SERVER VIEW DEFINITION ---");
        sb.AppendLine(view.ViewSQL.Trim());
        sb.AppendLine("*/");
        return sb.ToString();
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
                if (targetType == "INTEGER") targetType = "SERIAL";
                else if (targetType == "BIGINT") targetType = "BIGSERIAL";
            }

            columnLine.Append($"    \"{col.ColumnName}\" {targetType}");

            if (col.IsIdentity && ts.PrimaryKey.Count == 1 && ts.PrimaryKey[0] == col.ColumnName)
            {
                if (targetType != "SERIAL" && targetType != "BIGSERIAL")
                {
                    columnLine.Append(" PRIMARY KEY");
                }
            }

            if (!col.IsNullable)
            {
                columnLine.Append(" NOT NULL");
            }

            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                // --- THE FIX: Pass the targetType to the translator for context ---
                string defaultValue = TranslateSqlServerExpressionToPostgreSql(col.DefaultValue, targetType);
                columnLine.Append($" DEFAULT {defaultValue}");
            }
            allDefinitions.Add(columnLine.ToString());
        }

        if (ts.PrimaryKey.Count > 1)
        {
            var pkCols = string.Join(", ", ts.PrimaryKey.Select(c => $"\"{c}\""));
            allDefinitions.Add($"    PRIMARY KEY ({pkCols})");
        }

        sb.Append(string.Join(",\n", allDefinitions));
        sb.AppendLine("\n);");
        return sb.ToString();
    }

    // --- THE FIX: The method signature is updated to accept the target type ---
    private string TranslateSqlServerExpressionToPostgreSql(string expression, string targetPostgreSqlType)
    {
        if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
        string workExpression = expression.Trim();

        if (workExpression.StartsWith("(") && workExpression.EndsWith(")"))
        {
            workExpression = workExpression.Substring(1, workExpression.Length - 2);
        }
        if (workExpression.StartsWith("(") && workExpression.EndsWith(")"))
        {
            workExpression = workExpression.Substring(1, workExpression.Length - 2);
        }

        // --- THE FIX: Add specific logic for BOOLEAN types ---
        if (targetPostgreSqlType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase))
        {
            if (workExpression == "1") return "true";
            if (workExpression == "0") return "false";
        }

        if (workExpression.Equals("getdate()", StringComparison.OrdinalIgnoreCase) || workExpression.Equals("sysdatetime()", StringComparison.OrdinalIgnoreCase))
        {
            return "NOW()";
        }
        if (workExpression.Equals("newid()", StringComparison.OrdinalIgnoreCase))
        {
            return "gen_random_uuid()";
        }
        if (decimal.TryParse(workExpression, out _))
        {
            return workExpression;
        }

        var match = Regex.Match(workExpression, @"^N?'(.*)'$");
        if (match.Success)
        {
            return $"'{match.Groups[1].Value.Replace("'", "''")}'";
        }

        return workExpression;
    }
}
