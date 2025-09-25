
/// <summary>
/// Represents a UNIQUE constraint from the source database.
/// </summary>
public class UniqueConstraintSchema
{
    public string ConstraintName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
}
