public class ForeignKeySchema
{
    /// <summary>
    /// The name of the table that contains the foreign key.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the column that is the foreign key.
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the table that the foreign key references.
    /// </summary>
    public string ForeignTableName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the column in the foreign table that is being referenced.
    /// </summary>
    public string ForeignColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if the constraint uses CASCADE on delete.
    /// </summary>
    public bool CascadeOnDelete { get; set; }

    /// <summary>
    /// Indicates if the foreign key column is nullable.
    /// </summary>
    public bool IsNullable { get; set; }
}