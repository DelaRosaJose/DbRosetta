using System.Data.Common;

public interface IDatabaseSchemaReader
{
    Task<List<TableSchema>> GetTablesAsync(DbConnection connection);
}
