/// <summary>
/// Defines a contract for a class that can translate a database-specific
/// expression (like a DEFAULT value or a CHECK clause) into another dialect.
/// </summary>
public interface IExpressionTranslator
{
    /// <summary>
    /// Translates the given expression string.
    /// </summary>
    /// <param name="sourceExpression">The original expression from the source database.</param>
    /// <returns>A translated expression string suitable for the target database.</returns>
    string Translate(string sourceExpression);
}