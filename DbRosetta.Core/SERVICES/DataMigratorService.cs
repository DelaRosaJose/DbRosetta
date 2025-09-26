using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data.Common;

public class DataMigrator : IDataMigrator
{
    // Define constants for the SQL Server UDT full type names to avoid magic strings
    private const string SqlHierarchyIdTypeName = "Microsoft.SqlServer.Types.SqlHierarchyId";
    private const string SqlGeographyTypeName = "Microsoft.SqlServer.Types.SqlGeography";
    private const string SqlGeometryTypeName = "Microsoft.SqlServer.Types.SqlGeometry";

    public async Task MigrateDataAsync(
        DbConnection sourceConnection,
        DbConnection destinationConnection,
        List<TableSchema> tables,
        Action<string, long> progressAction)
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
            throw new NotSupportedException($"The destination database type '{destinationConnection.GetType().Name}' is not supported for data migration.");
        }
    }

    private async Task MigrateDataToPostgreSqlAsync(
        DbConnection sourceConnection,
        NpgsqlConnection destinationConnection,
        List<TableSchema> tables,
        Action<string, long> progressAction)
    {
        foreach (var table in tables)
        {
            long rowsMigrated = 0;
            var selectCommand = sourceConnection.CreateCommand();
            selectCommand.CommandText = $"SELECT * FROM [{table.TableSchemaName}].[{table.TableName}]";

            await using var reader = await selectCommand.ExecuteReaderAsync();

            var columnNames = string.Join(", ", table.Columns.Select(c => $"\"{c.ColumnName}\""));

            await using (var importer = await destinationConnection.BeginBinaryImportAsync($"COPY \"{table.TableName}\" ({columnNames}) FROM STDIN (FORMAT BINARY)"))
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
                            // --- THIS IS THE FIX ---
                            // Check for SQL Server User-Defined Types and convert them to strings.
                            string? valueTypeName = value.GetType().FullName;
                            if (valueTypeName == SqlHierarchyIdTypeName ||
                                valueTypeName == SqlGeographyTypeName ||
                                valueTypeName == SqlGeometryTypeName)
                            {
                                value = value.ToString();
                            }

                            if (value is TimeSpan timeSpanValue)
                            {
                                value = TimeOnly.FromTimeSpan(timeSpanValue);
                            }
                            else if (value is string stringValue)
                            {
                                value = stringValue.Trim();
                            }
                            await importer.WriteAsync(value);
                        }
                    }
                    rowsMigrated++;
                    if (rowsMigrated % 500 == 0)
                    {
                        progressAction(table.TableName, rowsMigrated);
                    }
                }
                await importer.CompleteAsync();
            }
            progressAction(table.TableName, rowsMigrated);
        }
    }

    // The MigrateDataToSqliteAsync method and other helpers remain unchanged.
    private async Task MigrateDataToSqliteAsync(DbConnection sourceConnection, SqliteConnection destinationConnection, List<TableSchema> tables, Action<string, long> progressAction) { /* ... unchanged ... */ }
    private DbCommand BuildInsertCommand(DbConnection connection, TableSchema table) { /* ... unchanged ... */ return connection.CreateCommand(); }
    private string NormalizeParameterName(string columnName) { /* ... unchanged ... */ return ""; }
}
