using DbRosetta.Core.Models;
using System.Data.Common;

namespace DbRosetta.Core.Interfaces
{
    /// <summary>
    /// Interface for writing data rows to a database table in a universal format.
    /// </summary>
    public interface IDataWriter
    {
        /// <summary>
        /// Writes a collection of data rows to the specified table.
        /// </summary>
        /// <param name="connection">An open connection to the target database.</param>
        /// <param name="table">The table schema to write data to.</param>
        /// <param name="data">The data rows to write.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data);
    }
}