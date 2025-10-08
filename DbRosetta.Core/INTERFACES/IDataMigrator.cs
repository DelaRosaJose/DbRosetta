using System.Data.Common;

namespace DbRosetta.Core.Interfaces
{
    /// <summary>
    /// Interface for migrating data from source to destination database.
    /// </summary>
    public interface IDataMigrator
    {
        /// <summary>
        /// Migrates data for the specified tables from source to destination.
        /// </summary>
        /// <param name="sourceConnection">The source database connection.</param>
        /// <param name="destinationConnection">The destination database connection.</param>
        /// <param name="tables">The list of tables to migrate.</param>
        /// <param name="progressAction">Action to report progress.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task MigrateDataAsync(DbConnection sourceConnection, DbConnection destinationConnection, List<TableSchema> tables, Func<string, int, Task> progressAction);
    }
}
