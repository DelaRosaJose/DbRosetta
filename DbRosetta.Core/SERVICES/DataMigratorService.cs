using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data;
using System.Data.Common;

public class DataMigrator // No longer needs IDataMigrator if this is the only implementation
{
    private const string SqlHierarchyIdTypeName = "Microsoft.SqlServer.Types.SqlHierarchyId";
    private const string SqlGeographyTypeName = "Microsoft.SqlServer.Types.SqlGeography";
    private const string SqlGeometryTypeName = "Microsoft.SqlServer.Types.SqlGeometry";
    private const int ProgressReportBatchSize = 500; // Use a constant for batch size

    /// <summary>
    /// Dispatches the migration to the appropriate provider based on the destination connection.
    /// The progressAction delegate is now async.
    /// </summary>
    public async Task MigrateDataAsync(
        DbConnection sourceConnection,
        DbConnection destinationConnection,
        List<TableSchema> tables,
        Func<string, int, Task> progressAction) // <-- CRITICAL: Signature changed to Func<..., Task>
    {
        if (destinationConnection is SqliteConnection sqliteConnection)
        {
            await MigrateDataToSqliteAsync(sourceConnection, sqliteConnection, tables, progressAction);
        }
        else if (destinationConnection is NpgsqlConnection npgsqlConnection)
        {
            await MigrateDataToPostgreSqlAsync(sourceConnection, npgsqlConnection, tables, progressAction);
        }
        else
        {
            throw new NotSupportedException($"The destination database type '{destinationConnection.GetType().Name}' is not supported.");
        }
    }

    private async Task MigrateDataToPostgreSqlAsync(
        DbConnection sourceConnection,
        NpgsqlConnection destinationConnection,
        List<TableSchema> tables,
        Func<string, int, Task> progressAction) // <-- Signature changed
    {
        foreach (var table in tables)
        {
            // Skip tables with no columns (e.g., if schema parsing had an issue)
            if (!table.Columns.Any()) continue;

            int rowsMigrated = 0;
            var selectCommand = sourceConnection.CreateCommand();

            // RECOMMENDATION: Build a robust SELECT statement to guarantee column order.
            var quotedSourceColumns = string.Join(", ", table.Columns.Select(c => $"[{c.ColumnName}]"));
            selectCommand.CommandText = $"SELECT {quotedSourceColumns} FROM [{table.TableSchemaName}].[{table.TableName}]";

            try
            {
                // Use SequentialAccess for better memory efficiency with large binary/text columns.
                await using var reader = await selectCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

                var pgColumnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));
                var copyCommand = $"COPY \"{table.TableName}\" ({pgColumnNames}) FROM STDIN (FORMAT BINARY)";

                await using (var importer = await destinationConnection.BeginBinaryImportAsync(copyCommand))
                {
                    while (await reader.ReadAsync())
                    {
                        await importer.StartRowAsync();
                        for (int i = 0; i < table.Columns.Count; i++)
                        {
                            var value = reader[i];
                            if (value is DBNull || value == null)
                            {
                                await importer.WriteNullAsync();
                            }
                            else
                            {
                                string? valueTypeName = value.GetType().FullName;
                                if (valueTypeName == SqlHierarchyIdTypeName ||
                                    valueTypeName == SqlGeographyTypeName ||
                                    valueTypeName == SqlGeometryTypeName)
                                {
                                    value = value.ToString();
                                }
                                else if (value is TimeSpan timeSpanValue)
                                {
                                    value = TimeOnly.FromTimeSpan(timeSpanValue);
                                }
                                else if (value is string stringValue)
                                {
                                    value = stringValue.TrimEnd(); // TrimEnd is slightly more efficient than Trim
                                }
                                await importer.WriteAsync(value);
                            }
                        }
                        rowsMigrated++;
                        if (rowsMigrated % ProgressReportBatchSize == 0)
                        {
                            // Await the async progress action
                            await progressAction(table.TableName, rowsMigrated);
                        }
                    }
                    await importer.CompleteAsync();
                }
                // Always report the final, total count for the table.
                await progressAction(table.TableName, rowsMigrated);
            }
            catch (Exception ex)
            {
                // Add more context to the exception for easier debugging.
                throw new Exception($"Failed to migrate data for table '{table.TableName}'. See inner exception for details.", ex);
            }
        }
    }

    private async Task MigrateDataToSqliteAsync(
     DbConnection sourceConnection,
     SqliteConnection destinationConnection,
     List<TableSchema> tables,
     Func<string, int, Task> progressAction)
    {
        await using var transaction = await destinationConnection.BeginTransactionAsync();

        foreach (var table in tables)
        {
            if (!table.Columns.Any()) continue;

            int rowsMigrated = 0;
            var quotedSourceColumns = string.Join(", ", table.Columns.Select(c => $"[{c.ColumnName}]"));
            var selectCommand = sourceConnection.CreateCommand();
            selectCommand.CommandText = $"SELECT {quotedSourceColumns} FROM [{table.TableSchemaName}].[{table.TableName}]";

            await using var reader = await selectCommand.ExecuteReaderAsync();
            await using var insertCommand = BuildSqliteInsertCommand(destinationConnection, table, transaction);

            while (await reader.ReadAsync())
            {
                insertCommand.Parameters.Clear();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var columnName = table.Columns[i].ColumnName;

                    // --- THIS IS THE FIX ---
                    // We create a new variable 'finalValue' to hold the sanitized data.
                    object finalValue;
                    var rawValue = reader[i];

                    if (rawValue is DBNull || rawValue == null)
                    {
                        finalValue = DBNull.Value;
                    }
                    else
                    {
                        string? valueTypeName = rawValue.GetType().FullName;
                        if (valueTypeName == SqlHierarchyIdTypeName ||
                            valueTypeName == SqlGeographyTypeName ||
                            valueTypeName == SqlGeometryTypeName)
                        {
                            // Explicitly convert to string
                            finalValue = rawValue.ToString() ?? string.Empty;
                        }
                        else if (rawValue is string stringValue)
                        {
                            finalValue = stringValue.TrimEnd();
                        }
                        else
                        {
                            // For all other types, use the raw value
                            finalValue = rawValue;
                        }
                    }

                    var parameter = new SqliteParameter($"@{NormalizeParameterName(columnName)}", finalValue);
                    insertCommand.Parameters.Add(parameter);
                }

                await insertCommand.ExecuteNonQueryAsync();
                rowsMigrated++;

                if (rowsMigrated % ProgressReportBatchSize == 0)
                {
                    await progressAction(table.TableName, rowsMigrated);
                }
            }
            await progressAction(table.TableName, rowsMigrated);
        }

        await transaction.CommitAsync();
    }

    // --- THIS IS THE CORRECTED HELPER METHOD ---
    private SqliteCommand BuildSqliteInsertCommand(
        SqliteConnection connection,
        TableSchema table,
        DbTransaction transaction) // <-- Changed back to the generic DbTransaction
    {
        var columnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));
        var parameterNames = string.Join(", ", table.Columns.Select(c => $"@{NormalizeParameterName(c.ColumnName)}"));

        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"INSERT INTO \"{table.TableName}\" ({columnNames}) VALUES ({parameterNames});";
        return command;
    }


    private string NormalizeParameterName(string columnName)
    {
        return columnName.Replace(" ", "").Replace("-", "");
    }
}