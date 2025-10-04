/// <summary>
/// Represents a binary operation, like >, =, OR, AND.
/// </summary>
public class OperatorNode : ExpressionNode
{
    public string Operator { get; set; } = string.Empty;
    public ExpressionNode? Left { get; set; }
    public ExpressionNode? Right { get; set; }
}