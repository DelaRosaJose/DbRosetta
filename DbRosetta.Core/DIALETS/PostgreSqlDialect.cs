using System.Data;

public class PostgreSqlDialect : IDatabaseDialect
{
    public string Name => "PostgreSql";

    public IDatabaseTypeMapper GetTypeMapper() => new PostgreSqlTypeMapper();
}

public class PostgreSqlTypeMapper : IDatabaseTypeMapper
{
    private static readonly Dictionary<string, DbType> TypeNameToDbTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bigint", DbType.Int64 },
        { "integer", DbType.Int32 },
        { "smallint", DbType.Int16 },
        { "boolean", DbType.Boolean },
        { "character varying", DbType.String },
        { "varchar", DbType.String },
        { "character", DbType.StringFixedLength },
        { "char", DbType.StringFixedLength },
        { "text", DbType.String },
        { "numeric", DbType.Decimal },
        { "decimal", DbType.Decimal },
        { "real", DbType.Single },
        { "double precision", DbType.Double },
        { "timestamp", DbType.DateTime },
        { "timestamp without time zone", DbType.DateTime },
        { "date", DbType.Date },
        { "time", DbType.Time },
        { "uuid", DbType.Guid },
        { "bytea", DbType.Binary },
        { "json", DbType.Object },
        { "jsonb", DbType.Object }
    };

    public DbType MapToGenericType(string dbTypeName)
    {
        return TypeNameToDbTypeMap.TryGetValue(dbTypeName, out var dbType) ? dbType : DbType.Object;
    }

    public string MapFromGenericType(DbType genericType, DbColumnInfo columnInfo)
    {
        return genericType switch
        {
            DbType.Int64 => "BIGINT",
            DbType.Int32 => "INTEGER",
            DbType.Int16 => "SMALLINT",
            DbType.Byte => "SMALLINT", // PostgreSQL doesn't have a single-byte integer
            DbType.Boolean => "BOOLEAN",
            DbType.String => "TEXT", // TEXT is generally preferred over VARCHAR(n) in Postgres
            DbType.AnsiString or DbType.AnsiStringFixedLength or DbType.StringFixedLength => "TEXT",
            DbType.Decimal or DbType.Currency => $"NUMERIC({columnInfo.Precision}, {columnInfo.Scale})",
            DbType.Single => "REAL",
            DbType.Double => "DOUBLE PRECISION",
            DbType.DateTime or DbType.DateTime2 or DbType.Date => "TIMESTAMP WITHOUT TIME ZONE",
            DbType.Time => "TIME",
            DbType.Guid => "UUID",
            DbType.Binary => "BYTEA",
            _ => "TEXT"
        };
    }
}