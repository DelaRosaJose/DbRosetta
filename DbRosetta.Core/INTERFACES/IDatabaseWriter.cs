using System.Data.Common;

/// <summary>
/// Defines the contract for writing a database schema to a target database.
/// </summary>
public interface IDatabaseWriter
{
    /// <summary>
    /// Generates and executes the necessary SQL to create the database schema.
    /// </summary>
    /// <param name="connection">An open connection to the target database.</param>
    /// <param name="tables">The list of table schemas to create.</param>
    /// <param name="typeService">The service used for translating data types.</param>
    /// <param name="sourceDialectName">The name of the original source dialect.</param>
    Task WriteSchemaAsync(DbConnection connection, List<TableSchema> tables, TypeMappingService typeService, string sourceDialectName);
}
