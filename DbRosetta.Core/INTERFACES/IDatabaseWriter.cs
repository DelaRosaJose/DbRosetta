using System.Data.Common;

public interface IDatabaseWriter
{
    /// <summary>
    /// Generates and executes the necessary SQL to create the database tables and related objects.
    /// </summary>
    Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName);

    /// <summary>
    /// Generates and executes the necessary SQL to create the database views.
    /// </summary>
    /// <param name="connection">An open connection to the target database.</param>
    /// <param name="views">The list of view schemas to create.</param>
    Task WriteViewsAsync(DbConnection connection, List<ViewSchema> views);

    /// <summary>
    /// Applies constraints and indexes after data has been loaded.
    /// This is an optional method for writers that support deferred index creation.
    /// </summary>
    Task WriteConstraintsAndIndexesAsync(DbConnection connection)
    {
        // Default implementation does nothing, so existing writers don't need to change.
        return Task.CompletedTask;
    }
}