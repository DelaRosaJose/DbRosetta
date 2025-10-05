using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Reading;
using DbRosetta.Core.Writers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbRosetta.Core.Services
{
    public class DatabaseProviderFactory
    {
        public IDatabaseSchemaReader GetSchemaReader(DatabaseEngine engine)
        {
            return engine switch
            {
                DatabaseEngine.SqlServer => new SqlServerSchemaReader(),
                DatabaseEngine.PostgreSql => new PostgreSqlSchemaReader(),
                _ => throw new NotSupportedException($"Schema reader for {engine} is not supported.")
            };
        }

        public IDatabaseSchemaWriter GetSchemaWriter(DatabaseEngine engine)
        {
            return engine switch
            {
                DatabaseEngine.SQLite => new SQLiteWriter(),
                DatabaseEngine.PostgreSql => new PostgreSqlWriter(),
                _ => throw new NotSupportedException($"Schema writer for {engine} is not supported.")
            };
        }

        public IDataReader GetDataReader(DatabaseEngine engine)
        {
            return engine switch
            {
                DatabaseEngine.SqlServer => new SqlServerDataReader(),
                DatabaseEngine.PostgreSql => new PostgreSqlDataReader(),
                DatabaseEngine.SQLite => new SQLiteDataReader(),
                DatabaseEngine.MySQL => new MySqlDataReader(), // Example: Add MySQL support
                _ => throw new NotSupportedException($"Data reader for {engine} is not supported.")
            };
        }

        public IDataWriter GetDataWriter(DatabaseEngine engine)
        {
            return engine switch
            {
                DatabaseEngine.SqlServer => new SqlServerDataWriter(),
                DatabaseEngine.PostgreSql => new PostgreSqlDataWriter(NullLogger<PostgreSqlDataWriter>.Instance),
                DatabaseEngine.SQLite => new SQLiteDataWriter(NullLogger<SQLiteDataWriter>.Instance),
                DatabaseEngine.MySQL => new MySqlDataWriter(), // Example: Add MySQL support
                _ => throw new NotSupportedException($"Data writer for {engine} is not supported.")
            };
        }
    }
}