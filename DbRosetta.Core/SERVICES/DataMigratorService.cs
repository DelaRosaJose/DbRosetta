using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

public class DataMigrator : IDataMigrator
{
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

                        var finalValue = value;
                        string? valueTypeName = value?.GetType().FullName;
                        if (valueTypeName == SqlGeographyTypeName ||
                            valueTypeName == SqlGeometryTypeName ||
                            valueTypeName == SqlHierarchyIdTypeName)
                        {
                            finalValue = value?.ToString();
                        }

                        // --- FINAL FIX: Trim string values to remove padding from fixed-length types (char/nchar). ---
                        // This is the crucial fix that solves the paradox.
                        if (finalValue is string stringValue)
                        {
                            finalValue = stringValue.Trim();
                        }

                        var parameter = insertCommand.CreateParameter();
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
        sb.Append(string.Join(", ", table.Columns.Select(c => NormalizeParameterName(c.ColumnName))));
        sb.Append(");");
        command.CommandText = sb.ToString();
        return command;
    }

    private string NormalizeParameterName(string columnName)
    {
        return "@" + Regex.Replace(columnName, @"[^\w]", "_");
    }
}
