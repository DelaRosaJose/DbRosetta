using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Models;
using System.Data.Common;

namespace DbRosetta.Core.Writers
{
    public class MySqlDataWriter : IDataWriter
    {
        public async Task WriteDataAsync(DbConnection connection, TableSchema table, IEnumerable<UniversalDataRow> data)
        {
            // TODO: Implement MySQL-specific data writing logic using MySqlConnector
            // For now, do nothing
            await Task.CompletedTask;
        }
    }
}