/// <summary>
/// Represents a database identifier, such as a column name.
/// </summary>
public class IdentifierNode : ExpressionNode
{
    public string Name { get; set; } = string.Empty;
}