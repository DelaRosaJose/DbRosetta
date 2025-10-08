using DbRosetta.Core;
using System.Data.Common;

public interface IDatabaseSchemaWriter
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
    Task WriteConstraintsAndIndexesAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        // Default implementation does nothing, so existing writers don't need to change.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs any database-specific optimizations or preparations before data migration.
    /// This is an optional method for writers that need pre-migration setup.
    /// </summary>
    Task PreMigrationAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        // Default implementation does nothing.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reverts any database-specific optimizations or performs cleanup after data migration.
    /// This is an optional method for writers that need post-migration cleanup.
    /// </summary>
    Task PostMigrationAsync(DbConnection connection, IMigrationProgressHandler progressHandler)
    {
        // Default implementation does nothing.
        return Task.CompletedTask;
    }
}