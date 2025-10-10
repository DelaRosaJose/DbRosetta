/// <summary>
/// The abstract base class for all nodes in an expression tree (AST).
/// This is the core of the "universal model".
/// </summary>
public abstract class ExpressionNode { }

/// <summary>
/// Represents a literal value like a number, string, or boolean.
/// </summary>
public class LiteralNode : ExpressionNode
{
    public object? Value { get; set; }
}

/// <summary>
/// Represents a binary operation, like >, =, OR, AND.
/// </summary>
public class OperatorNode : ExpressionNode
{
    public string Operator { get; set; } = string.Empty;
    public ExpressionNode? Left { get; set; }
    public ExpressionNode? Right { get; set; }
}

/// <summary>
/// Represents a database identifier, such as a column name.
/// </summary>
public class IdentifierNode : ExpressionNode
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Represents a universal function call, like getting the current time or a new GUID.
/// </summary>
public class FunctionCallNode : ExpressionNode
{
    /// <summary>
    /// A universal, dialect-agnostic name for the function's purpose.
    /// e.g., "GetCurrentTimestamp", "GenerateUuid"
    /// </summary>
    public string UniversalFunctionName { get; set; } = string.Empty;
    public List<ExpressionNode> Arguments { get; set; } = new();
}