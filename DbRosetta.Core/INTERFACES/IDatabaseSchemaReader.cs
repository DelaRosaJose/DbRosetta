using System.Data.Common;

public interface IDatabaseSchemaReader
{
    Task<List<TableSchema>> GetTablesAsync(DbConnection connection);

    /// <summary>
    /// Reads only the view definitions from the database.
    /// </summary>
    /// <param name="connection">An open connection to the source database.</param>
    /// <returns>A list of ViewSchema objects.</returns>
    Task<List<ViewSchema>> GetViewsAsync(DbConnection connection);
}