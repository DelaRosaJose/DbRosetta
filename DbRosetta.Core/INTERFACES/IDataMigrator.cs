using System.Data.Common;

/// <summary>
/// Defines the contract for migrating data from a source to a destination database.
/// </summary>
public interface IDataMigrator
{
    /// <summary>
    /// Migrates data for a given list of tables.
    /// </summary>
    /// <param name="sourceConnection">An open connection to the source database.</param>
    /// <param name="destinationConnection">An open connection to the destination database.</param>
    /// <param name="tables">The schema of the tables whose data needs to be migrated.</param>
    /// <param name="progressAction">An action to report progress on rows migrated per table.</param>
    Task MigrateDataAsync(
        DbConnection sourceConnection,
        DbConnection destinationConnection,
        List<TableSchema> tables,
        Action<string, long> progressAction);
}
