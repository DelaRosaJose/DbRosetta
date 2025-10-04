using DbRosetta.Core.Models;
using System.Data.Common;

namespace DbRosetta.Core.Interfaces
{
    /// <summary>
    /// Interface for reading data rows from a database table in a universal format.
    /// </summary>
    public interface IDataReader
    {
        /// <summary>
        /// Reads all data rows from the specified table.
        /// </summary>
        /// <param name="connection">An open connection to the source database.</param>
        /// <param name="table">The table schema to read data from.</param>
        /// <returns>An asynchronous enumerable of UniversalDataRow objects.</returns>
        IAsyncEnumerable<UniversalDataRow> ReadDataAsync(DbConnection connection, TableSchema table);
    }
}