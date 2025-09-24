using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions; // Required for parameter name normalization
using Microsoft.Data.Sqlite; // Required for SQLite-specific PRAGMA commands

    public class DataMigrator : IDataMigrator
    {
        // Define the type names as constants for clarity and maintainability
        private const string SqlGeographyTypeName = "Microsoft.SqlServer.Types.SqlGeography";
        private const string SqlGeometryTypeName = "Microsoft.SqlServer.Types.SqlGeometry";
        private const string SqlHierarchyIdTypeName = "Microsoft.SqlServer.Types.SqlHierarchyId";

        public async Task MigrateDataAsync(
            DbConnection sourceConnection,
            DbConnection destinationConnection,
            List<TableSchema> tables,
            Action<string, long> progressAction)
        {
            if (!(destinationConnection is SqliteConnection sqliteConnection))
            {
                throw new ArgumentException("A SqliteConnection is required for this data migrator.", nameof(destinationConnection));
            }

            // FIX 1: Disable foreign keys before starting the bulk data load to solve dependency order issues.
            var pragmaCommand = sqliteConnection.CreateCommand();
            pragmaCommand.CommandText = "PRAGMA foreign_keys = OFF;";
            await pragmaCommand.ExecuteNonQueryAsync();

            try
            {
                foreach (var table in tables)
                {
                    long rowsMigrated = 0;

                    var selectCommand = sourceConnection.CreateCommand();
                    selectCommand.CommandText = $"SELECT * FROM [{table.TableSchemaName}].[{table.TableName}]";

                    // This now builds a command with sanitized parameter names
                    var insertCommand = BuildInsertCommand(destinationConnection, table);

                    await using var reader = await selectCommand.ExecuteReaderAsync();
                    await using var transaction = await destinationConnection.BeginTransactionAsync();
                    insertCommand.Transaction = transaction;

                    while (await reader.ReadAsync())
                    {
                        insertCommand.Parameters.Clear();
                        for (int i = 0; i < table.Columns.Count; i++)
                        {
                            var columnName = table.Columns[i].ColumnName;
                            var value = reader[columnName];

                            // FIX 2: Intercept and convert special User-Defined Types to strings
                            var finalValue = value;
                            string? valueTypeName = value?.GetType().FullName;
                            if (valueTypeName == SqlGeographyTypeName ||
                                valueTypeName == SqlGeometryTypeName ||
                                valueTypeName == SqlHierarchyIdTypeName)
                            {
                                finalValue = value?.ToString();
                            }

                            var parameter = insertCommand.CreateParameter();

                            // FIX 3: Use the NORMALIZED name for the parameter object
                            parameter.ParameterName = NormalizeParameterName(columnName);

                            parameter.Value = finalValue is DBNull || finalValue is null ? DBNull.Value : finalValue;
                            insertCommand.Parameters.Add(parameter);
                        }

                        await insertCommand.ExecuteNonQueryAsync();
                        rowsMigrated++;

                        if (rowsMigrated % 100 == 0)
                        {
                            progressAction(table.TableName, rowsMigrated);
                        }
                    }

                    await transaction.CommitAsync();
                    progressAction(table.TableName, rowsMigrated);
                }
            }
            finally
            {
                // ALWAYS re-enable foreign keys after the process, even if it fails.
                pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
                await pragmaCommand.ExecuteNonQueryAsync();
            }
        }

        private DbCommand BuildInsertCommand(DbConnection connection, TableSchema table)
        {
            var command = connection.CreateCommand();
            var sb = new StringBuilder();

            sb.Append($"INSERT INTO [{table.TableName}] (");
            sb.Append(string.Join(", ", table.Columns.Select(c => $"[{c.ColumnName}]")));
            sb.Append(") VALUES (");

            // FIX 3: Use the NORMALIZED names when building the VALUES clause
            sb.Append(string.Join(", ", table.Columns.Select(c => NormalizeParameterName(c.ColumnName))));

            sb.Append(");");
            command.CommandText = sb.ToString();
            return command;
        }

        /// <summary>
        /// Creates a safe parameter name from a column name by replacing invalid characters.
        /// This prevents SQL syntax errors for columns with spaces or special characters.
        /// </summary>
        /// <param name="columnName">The original column name.</param>
        /// <returns>A sanitized string suitable for use as a parameter name (e.g., "@Address_Line_1").</returns>
        private string NormalizeParameterName(string columnName)
        {
            // Prepends "@" and replaces any character that is not a letter, digit, or underscore with an underscore.
            return "@" + Regex.Replace(columnName, @"[^\w]", "_");
        }
    }
