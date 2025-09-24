using System.Data;

/// <summary>
/// Orchestrates type translation between different database dialects.
/// </summary>
public class TypeMappingService
{
    private readonly Dictionary<string, IDatabaseDialect> _dialects;

    public TypeMappingService(IEnumerable<IDatabaseDialect> dialects)
    {
        _dialects = dialects.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Translates a column's type from a source dialect to a destination dialect.
    /// </summary>
    /// <param name="sourceColumn">The column metadata from the source database.</param>
    /// <param name="sourceDialectName">The name of the source database (e.g., "SqlServer").</param>
    /// <param name="destinationDialectName">The name of the destination database (e.g., "PostgreSql").</param>
    /// <returns>A string representing the appropriate type in the destination database.</returns>
    public string? TranslateType(DbColumnInfo sourceColumn, string sourceDialectName, string destinationDialectName)
    {
        if (!_dialects.TryGetValue(sourceDialectName, out var sourceDialect))
        {
            throw new ArgumentException($"Source dialect '{sourceDialectName}' is not registered.");
        }

        if (!_dialects.TryGetValue(destinationDialectName, out var destinationDialect))
        {
            throw new ArgumentException($"Destination dialect '{destinationDialectName}' is not registered.");
        }

        var sourceMapper = sourceDialect.GetTypeMapper();
        var destinationMapper = destinationDialect.GetTypeMapper();

        // Step 1: Map the source-specific type to a generic DbType
        DbType genericType = sourceMapper.MapToGenericType(sourceColumn.TypeName);

        if (genericType == DbType.Object)
        {
            // Handle unknown types gracefully
            Console.WriteLine($"Warning: Unknown type '{sourceColumn.TypeName}' for dialect '{sourceDialectName}'.");
            return null; // Or return a default type like TEXT/VARCHAR
        }

        // Step 2: Map the generic DbType to the destination-specific type string
        string destinationType = destinationMapper.MapFromGenericType(genericType, sourceColumn);

        return destinationType;
    }
}