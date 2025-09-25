public class ColumnSchema
{
    public string ColumnName { get; set; } = string.Empty;
    public string ColumnType { get; set; } = string.Empty; // The original, source-specific data type
    public int Length { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsNullable { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsIdentity { get; set; }
    public bool? IsCaseSensitivite { get; set; }
}