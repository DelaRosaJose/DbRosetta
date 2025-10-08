/// <summary>
/// Defines a contract for a class that can parse a dialect-specific
/// expression string into the universal ExpressionNode AST.
/// </summary>
public interface IExpressionParser
{
    ExpressionNode Parse(string expression);
}