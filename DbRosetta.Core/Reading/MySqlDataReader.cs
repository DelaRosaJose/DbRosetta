using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using System.Data.Common;

namespace DbRosetta.Core.Reading
{
    public class MySqlDataReader : IDataReader
    {
        public async IAsyncEnumerable<UniversalDataRow> ReadDataAsync(DbConnection connection, TableSchema table)
        {
            // TODO: Implement MySQL-specific data reading logic using MySqlConnector
            // For now, yield break to indicate no data
            yield break;
        }
    }
}