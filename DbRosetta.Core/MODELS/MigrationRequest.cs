using System.Text.Json.Serialization;

public class MigrationRequest
{

    [JsonConverter(typeof(JsonStringEnumConverter<DatabaseEngine>))]
    public DatabaseEngine SourceDialect { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<DatabaseEngine>))]
    public DatabaseEngine DestinationDialect { get; set; }


    public required string SourceConnectionString { get; set; }
    public required string DestinationConnectionString { get; set; }
}

public enum DatabaseEngine
{
    SqlServer,
    SQLite,
    PostgreSql
}