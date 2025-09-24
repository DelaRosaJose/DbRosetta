using System.Data;

public class MySqlDialect : IDatabaseDialect
{
    public string Name => "MySql";
    public IDatabaseTypeMapper GetTypeMapper() => new MySqlTypeMapper();
}

public class MySqlTypeMapper : IDatabaseTypeMapper
{
    private static readonly Dictionary<string, DbType> TypeNameToDbTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bigint", DbType.Int64 },
        { "int", DbType.Int32 },
        { "smallint", DbType.Int16 },
        { "tinyint", DbType.Byte },
        { "bit", DbType.Boolean },
        { "varchar", DbType.String },
        { "char", DbType.StringFixedLength },
        { "text", DbType.String },
        { "longtext", DbType.String },
        { "decimal", DbType.Decimal },
        { "float", DbType.Single },
        { "double", DbType.Double },
        { "datetime", DbType.DateTime },
        { "date", DbType.Date },
        { "time", DbType.Time },
        { "timestamp", DbType.DateTime },
        { "binary", DbType.Binary },
        { "varbinary", DbType.Binary },
        { "blob", DbType.Binary },
        { "longblob", DbType.Binary }
    };

    public DbType MapToGenericType(string dbTypeName)
    {
        // MySQL's tinyint(1) is often used for booleans
        if (dbTypeName.Equals("tinyint(1)", StringComparison.OrdinalIgnoreCase)) return DbType.Boolean;

        // Extract base type name if length is specified (e.g., "varchar(255)")
        var baseTypeName = dbTypeName.Split('(')[0];

        return TypeNameToDbTypeMap.TryGetValue(baseTypeName, out var dbType) ? dbType : DbType.Object;
    }

    public string MapFromGenericType(DbType genericType, DbColumnInfo columnInfo)
    {
        return genericType switch
        {
            DbType.Int64 => "BIGINT",
            DbType.Int32 => "INT",
            DbType.Int16 => "SMALLINT",
            DbType.Byte => "TINYINT",
            DbType.Boolean => "BIT(1)",
            DbType.String => (columnInfo.Length > 0 && columnInfo.Length <= 65535) ? $"VARCHAR({columnInfo.Length})" : "LONGTEXT",
            DbType.AnsiString or DbType.AnsiStringFixedLength or DbType.StringFixedLength => $"CHAR({columnInfo.Length})",
            DbType.Decimal or DbType.Currency => $"DECIMAL({columnInfo.Precision}, {columnInfo.Scale})",
            DbType.Single => "FLOAT",
            DbType.Double => "DOUBLE",
            DbType.DateTime or DbType.DateTime2 or DbType.Date => "DATETIME",
            DbType.Time => "TIME",
            DbType.Guid => "CHAR(36)", // Common way to store GUIDs in MySQL
            DbType.Binary => (columnInfo.Length > 0 && columnInfo.Length <= 65535) ? $"VARBINARY({columnInfo.Length})" : "LONGBLOB",
            _ => "VARCHAR(255)"
        };
    }
}