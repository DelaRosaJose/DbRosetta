using System.Data;

/// <summary>
/// Defines the contract for a specific database dialect,
/// providing access to its type mapping rules.
/// </summary>
public interface IDatabaseDialect
{
    string Name { get; }
    IDatabaseTypeMapper GetTypeMapper();
}

/// <summary>
/// Defines the contract for mapping types between a specific
/// database and the generic .NET DbType enum.
/// </summary>
public interface IDatabaseTypeMapper
{
    /// <summary>
    /// Maps a database-specific type name to a generic DbType.
    /// </summary>
    DbType MapToGenericType(string dbTypeName);

    /// <summary>
    /// Maps a generic DbType to a database-specific type definition string.
    /// </summary>
    string MapFromGenericType(DbType genericType, DbColumnInfo columnInfo);
}