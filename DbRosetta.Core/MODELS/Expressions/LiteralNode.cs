/// <summary>
/// Represents a literal value like a number, string, or boolean.
/// </summary>
public class LiteralNode : ExpressionNode
{
    public object? Value { get; set; }
}