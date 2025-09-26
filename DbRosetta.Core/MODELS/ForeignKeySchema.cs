/// <summary>
/// Represents a foreign key constraint, supporting single or composite (multi-column) keys.
/// </summary>
public class ForeignKeySchema
{
    /// <summary>
    /// The name of the foreign key constraint itself (e.g., "FK_SalesOrderDetail_SalesOrderHeader").
    /// </summary>
    public string ForeignKeyName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the table that contains the foreign key.
    /// </summary>
    public string LocalTable { get; set; } = string.Empty;

    /// <summary>
    /// The list of column(s) in the local table that make up the foreign key.
    /// Most often one column, but can be multiple for composite keys.
    /// </summary>
    public List<string> LocalColumns { get; set; } = new();

    /// <summary>
    /// The name of the table that the foreign key references.
    /// </summary>
    public string ForeignTable { get; set; } = string.Empty;

    /// <summary>
    /// The list of column(s) in the foreign table that are being referenced.
    /// Must match the order and number of LocalColumns.
    /// </summary>
    public List<string> ForeignColumns { get; set; } = new();

    /// <summary>
    /// The action to take when a referenced row is deleted.
    /// (e.g., NO ACTION, CASCADE, SET NULL, SET DEFAULT)
    /// </summary>
    public string DeleteAction { get; set; } = "NO ACTION";

    /// <summary>
    /// The action to take when a referenced row is updated.
    /// </summary>
    public string UpdateAction { get; set; } = "NO ACTION";
}