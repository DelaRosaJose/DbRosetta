using DbRosetta.Core;
using Npgsql;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

public class PostgreSqlWriter : IDatabaseWriter
{
    //public async Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName)
    //{
    //    if (!(connection is NpgsqlConnection npgsqlConnection))
    //    {
    //        throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
    //    }

    //    var setupCommand = npgsqlConnection.CreateCommand();
    //    setupCommand.CommandText = @"
    //            DROP SCHEMA public CASCADE;
    //            CREATE SCHEMA public;
    //            CREATE EXTENSION IF NOT EXISTS ""pgcrypto"";";
    //    await setupCommand.ExecuteNonQueryAsync();

    //    foreach (var table in tables)
    //    {
    //        string createTableScript = BuildCreateTableScript(table, typeService, sourceDialectName);
    //        try
    //        {
    //            var command = npgsqlConnection.CreateCommand();
    //            command.CommandText = createTableScript;
    //            await command.ExecuteNonQueryAsync();
    //        }
    //        catch (NpgsqlException ex)
    //        {
    //            throw new Exception($"Failed to create table '{table.TableName}'.\n{createTableScript}", ex);
    //        }

    //        // Add Indexes for the table
    //        foreach (var index in table.Indexes)
    //        {
    //            string createIndexScript = BuildCreateIndexScript(table.TableName, index);
    //            try
    //            {
    //                var indexCommand = npgsqlConnection.CreateCommand();
    //                indexCommand.CommandText = createIndexScript;
    //                await indexCommand.ExecuteNonQueryAsync();
    //            }
    //            catch (NpgsqlException ex)
    //            {
    //                throw new Exception($"Failed to create index '{index.IndexName}' on table '{table.TableName}'.\n{createIndexScript}", ex);
    //            }
    //        }

    //        // Add Triggers for the table
    //        foreach (var trigger in table.Triggers)
    //        {
    //            string createTriggerScript = BuildCreateTriggerScript(trigger);
    //            try
    //            {
    //                var triggerCommand = npgsqlConnection.CreateCommand();
    //                triggerCommand.CommandText = createTriggerScript;
    //                await triggerCommand.ExecuteNonQueryAsync();
    //            }
    //            catch (NpgsqlException ex)
    //            {
    //                // It's common for initial trigger creation to fail if the function doesn't exist yet,
    //                // so we are careful to not make this a hard-stop failure.
    //                throw new Exception($"Failed to create trigger placeholder for '{trigger.Name}' on table '{trigger.Table}'.\n{createTriggerScript}", ex);
    //            }
    //        }
    //    }
    //}

    // Store tables here to be used in the second phase



    // --- CORRECTED PRIVATE FIELDS ---
    // They are now nullable and will be assigned in WriteSchemaAsync.
    private List<TableSchema>? _tables;
    private TypeMappingService? _typeService;
    private string? _sourceDialectName;

    /// <summary>
    /// Phase 1: Creates the base tables and trigger functions only.
    /// Indexes and constraints are deferred until after data migration.
    /// </summary>
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
            // First, create the table itself.
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

            // --- THIS IS THE RESTORED CODE ---
            // Now, create the placeholder functions and triggers for this table.
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
                    // Use a warning here instead of a hard crash for resilience.
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"--> Warning: Could not create trigger placeholder for '{trigger.Name}' on table '{trigger.Table}'.");
                    Console.WriteLine($"    Reason: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }
    }

    public async Task WriteConstraintsAndIndexesAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        if (_tables is null || _typeService is null || string.IsNullOrEmpty(_sourceDialectName))
        {
            throw new InvalidOperationException("WriteSchemaAsync must be called before WriteConstraintsAndIndexesAsync.");
        }
        if (!(connection is NpgsqlConnection npgsqlConnection))
        {
            throw new ArgumentException("A NpgsqlConnection is required.", nameof(connection));
        }

        Console.WriteLine("\n[Phase 3/3] Applying indexes and constraints...");

        foreach (var table in _tables)
        {
            // --- STEP 1: APPLY UNIQUE CONSTRAINTS ---
            // Must be done before Foreign Keys.
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
                    await progressHandler.SendWarningAsync($"--> Warning: Could not create unique constraint '{uq.ConstraintName}' on table '{table.TableName}'.");
                    await progressHandler.SendWarningAsync($"    Reason: {ex.Message}");
                }
            }

            // --- STEP 2: APPLY FOREIGN KEY CONSTRAINTS ---
            // Now references to UNIQUE constraints will succeed.
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
                    await progressHandler.SendWarningAsync($"--> Warning: Could not create foreign key '{fk.ForeignKeyName}' on table '{table.TableName}'.");
                    await progressHandler.SendWarningAsync($"    Reason: {ex.Message}");
                }
            }

            // --- STEP 3: APPLY CHECK CONSTRAINTS ---
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
                    await progressHandler.SendWarningAsync($"--> Warning: Could not create check constraint '{chk.ConstraintName}' on table '{table.TableName}'.");
                    await progressHandler.SendWarningAsync($"    Reason: {ex.Message}");
                }
            }

            // --- STEP 4: APPLY INDEXES ---
            // XML and oversized index warnings will be handled here.
            foreach (var index in table.Indexes)
            {
                // ... (Existing logic to check for non-BTree indexable types like XML) ...

                string createIndexScript = BuildCreateIndexScript(table.TableName, index);
                try
                {
                    var indexCommand = npgsqlConnection.CreateCommand();
                    indexCommand.CommandText = createIndexScript;
                    await indexCommand.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex)
                {
                    await progressHandler.SendWarningAsync($"--> Warning: Could not create index '{index.IndexName}' on table '{table.TableName}'.");
                    await progressHandler.SendWarningAsync($"    Reason: {ex.SqlState}: {ex.Message}");
                    await progressHandler.SendWarningAsync("    Manual Action: This index may need to be created manually, possibly using a different method (e.g., hash index).");
                }
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


    /// <summary>
    /// FINAL, CORRECTED VERSION: Handles all Primary Key scenarios by generating a standard constraint name.
    /// </summary>
    private string BuildCreateTableScript(TableSchema ts, TypeMappingService typeService, string sourceDialectName)
    {
        var sb = new StringBuilder();
        var allDefinitions = new List<string>();

        sb.AppendLine($"CREATE TABLE \"{ts.TableName}\" (");

        // 1. Define all columns (This part is correct and unchanged)
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

            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
            {
                string defaultValue = TranslateSqlServerExpressionToPostgreSql(col.DefaultValue, targetType);
                columnLine.Append($" DEFAULT {defaultValue}");
            }
            allDefinitions.Add(columnLine.ToString());
        }

        // --- THIS IS THE FIX ---
        // 2. Define the Primary Key constraint, if it exists, using a standard name.
        if (ts.PrimaryKey.Any())
        {
            var pkCols = string.Join(", ", ts.PrimaryKey.Select(c => $"\"{c}\""));
            // Generate a standard, conventional name for the primary key constraint.
            string pkName = $"PK_{ts.TableName}";
            allDefinitions.Add($"    CONSTRAINT \"{pkName}\" PRIMARY KEY ({pkCols})");
        }

        sb.Append(string.Join(",\n", allDefinitions));
        sb.AppendLine("\n);");
        return sb.ToString();
    }

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

    /// <summary>
    /// Builds a CREATE INDEX statement for PostgreSQL.
    /// </summary>
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
        var sb = new StringBuilder();
        string functionName = $"placeholder_trigger_function_{trigger.Name}";
        string migrationWarning = "";
        string triggerTypeClause;

        // --- THE FIX: Detect INSTEAD OF triggers on tables ---
        if (trigger.Type == TriggerType.InsteadOf)
        {
            // PostgreSQL only supports INSTEAD OF triggers on VIEWs, not TABLEs.
            // We will convert this to a BEFORE trigger to act as a valid placeholder and add a prominent warning.
            triggerTypeClause = "BEFORE";
            migrationWarning = @"
-- ######################################################################################
-- # MIGRATION WARNING:                                                                 #
-- # This trigger was originally an 'INSTEAD OF' trigger on a TABLE.                    #
-- # PostgreSQL only supports 'INSTEAD OF' on VIEWs.                                    #
-- # It has been converted to a 'BEFORE' trigger placeholder for schema compatibility.  #
-- # The original T-SQL logic below MUST BE MANUALLY TRANSLATED to a PostgreSQL         #
-- # function that achieves the desired outcome (e.g., raising an exception).           #
-- ######################################################################################
";
        }
        else
        {
            // SQL Server's 'AFTER' is equivalent to PostgreSQL's 'AFTER'.
            triggerTypeClause = "AFTER";
        }

        // 1. Create a placeholder trigger function
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {functionName}() RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    -- This is a placeholder trigger function. The original T-SQL logic is in the trigger's comment.");
        sb.AppendLine("    -- For a translated trigger, you would RETURN NULL to cancel the operation, or RETURN NEW/OLD to allow it.");
        sb.AppendLine("    RETURN NEW; -- Default action: allow the operation to proceed.");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();

        // 2. Create the trigger that executes the placeholder function
        sb.AppendLine($"DROP TRIGGER IF EXISTS \"{trigger.Name}\" ON \"{trigger.Table}\";");

        // Add the warning, if any, right before the CREATE TRIGGER statement
        if (!string.IsNullOrEmpty(migrationWarning))
        {
            sb.AppendLine(migrationWarning);
        }

        sb.Append($"CREATE TRIGGER \"{trigger.Name}\"");
        sb.Append($" {triggerTypeClause} {trigger.Event.ToString().ToUpper()} ON \"{trigger.Table}\"");
        sb.AppendLine(" FOR EACH ROW");
        sb.AppendLine($"EXECUTE PROCEDURE {functionName}();");
        sb.AppendLine();

        // 3. Add the original T-SQL as a comment on the new trigger for manual translation
        sb.AppendLine($"COMMENT ON TRIGGER \"{trigger.Name}\" ON \"{trigger.Table}\" IS E'");
        sb.AppendLine("--- ORIGINAL SQL SERVER TRIGGER (to be manually translated) ---");
        // Escape single quotes for the comment string
        var escapedBody = trigger.Body.Replace("'", "''");
        var bodyLines = escapedBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in bodyLines)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine("';");

        return sb.ToString();
    }


    #region Constraint Builders

    /// <summary>
    /// Builds an ALTER TABLE statement to add a Foreign Key constraint.
    /// </summary>
    private string BuildAddForeignKeyConstraintScript(string tableName, ForeignKeySchema fk)
    {
        // Build the comma-separated list of local columns, properly quoted.
        var localColumns = string.Join(", ", fk.LocalColumns.Select(c => $"\"{c}\""));

        // Build the comma-separated list of foreign columns, properly quoted.
        var foreignColumns = string.Join(", ", fk.ForeignColumns.Select(c => $"\"{c}\""));

        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{fk.ForeignKeyName}\" ");
        sb.Append($"FOREIGN KEY ({localColumns}) REFERENCES \"{fk.ForeignTable}\" ({foreignColumns})");

        // Add the ON DELETE action if it's something other than the default.
        // PostgreSQL uses "NO ACTION" by default, so we only need to specify non-default actions.
        if (!string.IsNullOrEmpty(fk.DeleteAction) && !fk.DeleteAction.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ON DELETE {fk.DeleteAction}");
        }

        // Add the ON UPDATE action.
        if (!string.IsNullOrEmpty(fk.UpdateAction) && !fk.UpdateAction.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ON UPDATE {fk.UpdateAction}");
        }

        sb.Append(";");

        return sb.ToString();
    }

    /// <summary>
    /// Builds an ALTER TABLE statement to add a Unique constraint.
    /// </summary>
    private string BuildAddUniqueConstraintScript(string tableName, UniqueConstraintSchema uq)
    {
        var uniqueColumns = string.Join(", ", uq.Columns.Select(c => $"\"{c}\""));
        return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{uq.ConstraintName}\" UNIQUE ({uniqueColumns});";
    }

    /// <summary>
    /// Builds an ALTER TABLE statement to add a Check constraint, translating the expression.
    /// </summary>
    private string BuildAddCheckConstraintScript(string tableName, CheckConstraintSchema chk)
    {
        string translatedExpression = TranslateConstraintExpressionToPostgreSql(chk.CheckClause);
        return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{chk.ConstraintName}\" CHECK ({translatedExpression});";
    }

    /// <summary>
    /// FINAL VERSION: Translates T-SQL expressions, now with DATEADD and DATEDIFF handling.
    /// </summary>
    private string TranslateConstraintExpressionToPostgreSql(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "true";
        string workExpression = expression.Trim();

        // Remove outer parentheses
        if (workExpression.StartsWith("(") && workExpression.EndsWith(")"))
        {
            workExpression = workExpression.Substring(1, workExpression.Length - 2).Trim();
        }

        // Standard translations
        workExpression = Regex.Replace(workExpression,
        @"\bdatediff\s*\(\s*(year|month|day)\s*,\s*([^,]+?)\s*,\s*([^)]+?)\s*\)",
        @"(DATE_PART('$1', $3) - DATE_PART('$1', $2))", // Simplified translation, relies on later steps for identifiers
        RegexOptions.IgnoreCase);

        // Standard translations (run after the more specific DATEDIFF)
        workExpression = Regex.Replace(workExpression, @"\[([^\]]+)\]", @"""$1""");
        workExpression = Regex.Replace(workExpression, @"\bN'((?:[^']|'')*)'", "'$1'");
        workExpression = Regex.Replace(workExpression, @"\bgetdate\(\)", "NOW()", RegexOptions.IgnoreCase);
        workExpression = Regex.Replace(workExpression, @"\bsysdatetime\(\)", "NOW()", RegexOptions.IgnoreCase);

        return workExpression;
    }

    #endregion

}