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
}