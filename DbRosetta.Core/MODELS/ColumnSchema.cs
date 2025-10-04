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