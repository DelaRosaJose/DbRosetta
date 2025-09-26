public class MigrationRequest
{
    public string SourceDialect { get; set; }
    public string SourceConnectionString { get; set; }
    public string DestinationDialect { get; set; }
    public string DestinationConnectionString { get; set; }
}