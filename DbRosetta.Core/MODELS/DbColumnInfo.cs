/// <summary>
/// Represents the metadata of a column from a source database.
/// </summary>
public class DbColumnInfo
{
    public string TypeName { get; set; } = string.Empty;
    public int Length { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
}