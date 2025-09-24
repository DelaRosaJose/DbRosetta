// DbRosetta.Core/DIALECTS/SQLiteDialect.cs

using System.Data;

public class SQLiteDialect : IDatabaseDialect
{
    public string Name => "SQLite";

    public IDatabaseTypeMapper GetTypeMapper() => new SQLiteTypeMapper();
}

public class SQLiteTypeMapper : IDatabaseTypeMapper
{
    // SQLite uses type affinity based on keywords in the declared type name.
    // It doesn't strictly enforce types, but affinity guides storage and comparison.
    private static readonly Dictionary<string, DbType> TypeNameToDbTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // INTEGER affinity
            { "integer", DbType.Int64 }, // SQLite's INTEGER is 8-byte signed integer
            { "int", DbType.Int32 },
            { "tinyint", DbType.Byte },
            { "smallint", DbType.Int16 },
            { "mediumint", DbType.Int32 },
            { "bigint", DbType.Int64 },
            { "unsigned big int", DbType.UInt64 },
            { "int2", DbType.Int16 },
            { "int8", DbType.Int64 },
            { "boolean", DbType.Boolean }, // Stored as 0 or 1, has NUMERIC affinity but behaves like INTEGER

            // TEXT affinity
            { "text", DbType.String },
            { "clob", DbType.String },
            { "char", DbType.StringFixedLength },
            { "varchar", DbType.String },
            { "nchar", DbType.StringFixedLength },
            { "nvarchar", DbType.String },

            // BLOB affinity
            { "blob", DbType.Binary },
            { "timestamp", DbType.Binary }, // SQL Server timestamp (rowversion) maps to BLOB

            // REAL affinity
            { "real", DbType.Double }, // SQLite's REAL is 8-byte floating point
            { "double", DbType.Double },
            { "double precision", DbType.Double },
            { "float", DbType.Single },

            // NUMERIC affinity (can store INTEGER, REAL, or TEXT)
            { "numeric", DbType.Decimal },
            { "decimal", DbType.Decimal },
            // { "boolean", DbType.Boolean }, // <-- THIS WAS THE DUPLICATE, IT IS NOW REMOVED
            { "date", DbType.Date }, // Stored as TEXT (ISO8601 strings)
            { "datetime", DbType.DateTime }, // Stored as TEXT (ISO8601 strings)
            { "time", DbType.Time }, // Stored as TEXT (ISO8601 strings)
            { "uniqueidentifier", DbType.Guid } // Stored as TEXT
        };

    public DbType MapToGenericType(string dbTypeName)
    {
        // Prioritize specific matches
        if (TypeNameToDbTypeMap.TryGetValue(dbTypeName, out var dbType))
        {
            return dbType;
        }

        // Check for affinity keywords as a fallback
        var lowerTypeName = dbTypeName.ToLowerInvariant();

        if (lowerTypeName.Contains("int")) return DbType.Int64; // INTEGER affinity
        if (lowerTypeName.Contains("char") || lowerTypeName.Contains("clob") || lowerTypeName.Contains("text")) return DbType.String; // TEXT affinity
        if (lowerTypeName.Contains("blob")) return DbType.Binary; // BLOB affinity
        if (lowerTypeName.Contains("real") || lowerTypeName.Contains("doub") || lowerTypeName.Contains("float")) return DbType.Double; // REAL affinity

        // For NUMERIC affinity, which is broad, let's be more careful.
        // If it's not a known type, we might treat it as a string to be safe.
        if (lowerTypeName.Contains("numeric") || lowerTypeName.Contains("decimal")) return DbType.Decimal;
        if (lowerTypeName.Contains("date") || lowerTypeName.Contains("time")) return DbType.DateTime;
        if (lowerTypeName.Contains("uuid") || lowerTypeName.Contains("guid")) return DbType.Guid;

        return DbType.Object; // Fallback for truly unknown types
    }

    public string MapFromGenericType(DbType genericType, DbColumnInfo columnInfo)
    {
        // When mapping *to* SQLite, we usually aim for the simplest, most compatible affinity.
        return genericType switch
        {
            DbType.Int64 or DbType.Int32 or DbType.Int16 or DbType.Byte or DbType.Boolean => "INTEGER",
            DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength => "TEXT",
            DbType.Double or DbType.Single => "REAL",
            DbType.Decimal or DbType.Currency => $"NUMERIC({columnInfo.Precision}, {columnInfo.Scale})", // SQLite handles precision/scale but stores as NUMERIC affinity
            DbType.Date or DbType.DateTime or DbType.DateTime2 or DbType.Time => "TEXT", // Stored as ISO8601 strings
            DbType.Guid => "TEXT", // GUIDs are typically stored as TEXT in SQLite
            DbType.Binary => "BLOB",
            _ => "TEXT" // Default fallback for unknown types
        };
    }
}
