public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public string TableSchemaName { get; set; } = string.Empty; // e.g., "dbo" in SQL Server
    public List<ColumnSchema> Columns { get; set; } = new();
    public List<string> PrimaryKey { get; set; } = new();
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
    public List<IndexSchema> Indexes { get; set; } = new();
    public List<string> CheckConstraints { get; set; } = new();
}