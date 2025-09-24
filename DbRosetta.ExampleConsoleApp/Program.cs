

var dialects = new List<IDatabaseDialect>
            {
                new SqlServerDialect(),
                new PostgreSqlDialect(),
                new MySqlDialect(),
                new SQLiteDialect() // Now includes SQLite!
            };

var typeService = new TypeMappingService(dialects);

// --- Examples of using the TypeMappingService ---

// SQL Server NVARCHAR(255) to SQLite TEXT
var sqlServerNvarchar = new DbColumnInfo { TypeName = "nvarchar", Length = 255 };
string sqliteType = typeService.TranslateType(sqlServerNvarchar, "SqlServer", "SQLite")!;
Console.WriteLine($"SQL Server 'nvarchar(255)' becomes '{sqliteType}' in SQLite."); // Expected: TEXT

// MySQL BIT(1) to PostgreSQL BOOLEAN
var mySqlBit = new DbColumnInfo { TypeName = "BIT(1)" };
string pgType = typeService.TranslateType(mySqlBit, "MySql", "PostgreSql")!;
Console.WriteLine($"MySQL 'BIT(1)' becomes '{pgType}' in PostgreSQL."); // Expected: BOOLEAN

// SQLite INTEGER to SQL Server INT
var sqliteInteger = new DbColumnInfo { TypeName = "INTEGER" };
string sqlServerType = typeService.TranslateType(sqliteInteger, "SQLite", "SqlServer")!;
Console.WriteLine($"SQLite 'INTEGER' becomes '{sqlServerType}' in SQL Server."); // Expected: INT

// PostgreSQL UUID to MySQL CHAR(36)
var pgUuid = new DbColumnInfo { TypeName = "uuid" };
string mySqlUuid = typeService.TranslateType(pgUuid, "PostgreSql", "MySql")!;
Console.WriteLine($"PostgreSQL 'uuid' becomes '{mySqlUuid}' in MySQL."); // Expected: CHAR(36)

// SQL Server DECIMAL(18,2) to PostgreSQL NUMERIC(18,2)
var sqlServerDecimal = new DbColumnInfo { TypeName = "decimal", Precision = 18, Scale = 2 };
string pgDecimal = typeService.TranslateType(sqlServerDecimal, "SqlServer", "PostgreSql")!;
Console.WriteLine($"SQL Server 'decimal(18,2)' becomes '{pgDecimal}' in PostgreSQL."); // Expected: NUMERIC(18, 2)