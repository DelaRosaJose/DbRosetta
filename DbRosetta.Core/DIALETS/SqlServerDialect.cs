using System;
using System.Data;

public class SqlServerDialect : IDatabaseDialect
{
    public string Name => "SqlServer";

    public IDatabaseTypeMapper GetTypeMapper() => new SqlServerTypeMapper();
}

public class SqlServerTypeMapper : IDatabaseTypeMapper
{
    private static readonly Dictionary<string, DbType> TypeNameToDbTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bigint", DbType.Int64 },
        { "binary", DbType.Binary },
        { "bit", DbType.Boolean },
        { "char", DbType.AnsiStringFixedLength },
        { "date", DbType.Date },
        { "datetime", DbType.DateTime },
        { "datetime2", DbType.DateTime2 },
        { "decimal", DbType.Decimal },
        { "float", DbType.Double },
        { "image", DbType.Binary },
        { "int", DbType.Int32 },
        { "money", DbType.Currency },
        { "nchar", DbType.StringFixedLength },
        { "ntext", DbType.String },
        { "numeric", DbType.Decimal },
        { "nvarchar", DbType.String },
        { "real", DbType.Single },
        { "smalldatetime", DbType.DateTime },
        { "smallint", DbType.Int16 },
        { "smallmoney", DbType.Currency },
        { "text", DbType.AnsiString },
        { "time", DbType.Time },
        { "timestamp", DbType.Binary },
        { "tinyint", DbType.Byte },
        { "uniqueidentifier", DbType.Guid },
        { "varbinary", DbType.Binary },
        { "varchar", DbType.AnsiString },
        { "xml", DbType.Xml },
        { "hierarchyid", DbType.String },
        { "geography", DbType.String },
        { "geometry", DbType.String }
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
            DbType.Boolean => "BIT",
            DbType.AnsiStringFixedLength => $"CHAR({columnInfo.Length})",
            DbType.DateTime or DbType.Date => "DATETIME2",
            DbType.Decimal or DbType.Currency => $"DECIMAL({columnInfo.Precision}, {columnInfo.Scale})",
            DbType.Double => "FLOAT",
            DbType.Int32 => "INT",
            DbType.StringFixedLength => $"NCHAR({columnInfo.Length})",
            DbType.String => (columnInfo.Length > 0 && columnInfo.Length <= 4000) ? $"NVARCHAR({columnInfo.Length})" : "NVARCHAR(MAX)",
            DbType.AnsiString => (columnInfo.Length > 0 && columnInfo.Length <= 8000) ? $"VARCHAR({columnInfo.Length})" : "VARCHAR(MAX)",
            DbType.Single => "REAL",
            DbType.Int16 => "SMALLINT",
            DbType.Byte => "TINYINT",
            DbType.Guid => "UNIQUEIDENTIFIER",
            DbType.Binary => (columnInfo.Length > 0 && columnInfo.Length <= 8000) ? $"VARBINARY({columnInfo.Length})" : "VARBINARY(MAX)",
            DbType.Xml => "XML",
            _ => "NVARCHAR(MAX)"
        };
    }
}