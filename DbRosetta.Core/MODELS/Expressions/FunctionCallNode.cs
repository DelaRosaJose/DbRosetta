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