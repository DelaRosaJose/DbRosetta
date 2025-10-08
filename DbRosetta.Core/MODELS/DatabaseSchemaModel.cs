
public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public string TableSchemaName { get; set; } = string.Empty; // e.g., "dbo" in SQL Server
    public List<ColumnSchema> Columns { get; set; } = new();
    public List<string> PrimaryKey { get; set; } = new();
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
    public List<IndexSchema> Indexes { get; set; } = new();
    public List<CheckConstraintSchema> CheckConstraints { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of UNIQUE constraints for this table.
    /// </summary>
    public List<UniqueConstraintSchema> UniqueConstraints { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of Triggers associated with this table.
    /// </summary>
    public List<TriggerSchema> Triggers { get; set; } = new();
}

public class ColumnSchema
{
    public string ColumnName { get; set; } = string.Empty;
    public string ColumnType { get; set; } = string.Empty; // The original, source-specific data type
    public int Length { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public bool? IsCaseSensitivite { get; set; }

    /// <summary>
    /// The original, unprocessed default value string from the source database.
    /// </summary>
    public string DefaultValueAsString { get; set; } = string.Empty;

    /// <summary>
    /// The parsed, universal Abstract Syntax Tree for the default value.
    /// </summary>
    public ExpressionNode? DefaultValueAst { get; set; }
}

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

public class IndexSchema
{
    public string IndexName { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public List<IndexColumn> Columns { get; set; } = new();
}

public class IndexColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public bool IsAscending { get; set; }
}

public class CheckConstraintSchema
{
    public string ConstraintName { get; set; } = string.Empty;

    // --- MODIFIED ---
    /// <summary>
    /// The original, unprocessed check clause string from the source database.
    /// </summary>
    public string CheckClauseAsString { get; set; } = string.Empty;

    /// <summary>
    /// The parsed, universal Abstract Syntax Tree for the check clause.
    /// </summary>
    public ExpressionNode? CheckClauseAst { get; set; }
}

public class UniqueConstraintSchema
{
    public string ConstraintName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
}

public enum TriggerEvent
{
    Delete,
    Update,
    Insert
}

public enum TriggerType
{
    After,
    Before,
    /// <summary>
    /// Represents an INSTEAD OF trigger, common in SQL Server.
    /// </summary>
    InsteadOf // This was the missing definition
}

public class TriggerSchema
{
    public string Name { get; set; } = string.Empty;
    public TriggerEvent Event { get; set; }
    public TriggerType Type { get; set; }
    public string Body { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
}

public class ViewSchema
{
    public string ViewName { get; set; } = string.Empty;
    public string ViewSQL { get; set; } = string.Empty;
}

public class DatabaseSchema
{
    public List<TableSchema> Tables { get; set; } = new();
    public List<ViewSchema> Views { get; set; } = new();
}
