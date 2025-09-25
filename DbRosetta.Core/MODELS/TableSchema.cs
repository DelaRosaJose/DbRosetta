public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public string TableSchemaName { get; set; } = string.Empty; // e.g., "dbo" in SQL Server
    public List<ColumnSchema> Columns { get; set; } = new();
    public List<string> PrimaryKey { get; set; } = new();
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
    public List<IndexSchema> Indexes { get; set; } = new();
    public List<string> CheckConstraints { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of UNIQUE constraints for this table.
    /// </summary>
    public List<UniqueConstraintSchema> UniqueConstraints { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of Triggers associated with this table.
    /// </summary>
    public List<TriggerSchema> Triggers { get; set; } = new();
}