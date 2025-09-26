/// <summary>
/// Represents a CHECK constraint in the database.
/// </summary>
public class CheckConstraintSchema
{
    public string ConstraintName { get; set; } = string.Empty;
    public string CheckClause { get; set; } = string.Empty;
}