using System.Text.RegularExpressions;

/// <summary>
/// Parses T-SQL expression strings into the universal ExpressionNode AST.
/// This implementation is simplified to handle common patterns found in
/// default values and check constraints, like GETDATE() or basic comparisons.
/// A full-featured, production-grade parser would require a dedicated
/// parsing library like ANTLR.
/// </summary>
public class TSqlExpressionParser : IExpressionParser
{
    public ExpressionNode Parse(string expression)
    {
        // Sanitize the input by removing outer parentheses that often wrap
        // default constraints, e.g., ((getdate())) -> getdate()
        string workExpression = SanitizeExpression(expression);

        // --- Function Calls ---
        if (Regex.IsMatch(workExpression, @"^(getdate|sysdatetime)\(\)$", RegexOptions.IgnoreCase))
        {
            return new FunctionCallNode { UniversalFunctionName = "GetCurrentTimestamp" };
        }
        if (Regex.IsMatch(workExpression, @"^dateadd\s*\(\s*(\w+)\s*,\s*(.+?)\s*,\s*(.+?)\s*\)$", RegexOptions.IgnoreCase))
        {
            var match = Regex.Match(workExpression, @"^dateadd\s*\(\s*(\w+)\s*,\s*(.+?)\s*,\s*(.+?)\s*\)$", RegexOptions.IgnoreCase);
            string part = match.Groups[1].Value;
            string numberStr = match.Groups[2].Value.Trim();
            string dateStr = match.Groups[3].Value.Trim();
            ExpressionNode partNode = new LiteralNode { Value = part };
            ExpressionNode numberNode = Parse(numberStr);
            ExpressionNode dateNode = Parse(dateStr);
            return new FunctionCallNode { UniversalFunctionName = "dateadd", Arguments = new List<ExpressionNode> { partNode, numberNode, dateNode } };
        }
        if (Regex.IsMatch(workExpression, @"^newid\(\)$", RegexOptions.IgnoreCase))
        {
            return new FunctionCallNode { UniversalFunctionName = "GenerateUuid" };
        }

        // --- Literals ---
        // Boolean: CONVERT([bit],(1)) or CONVERT(bit, 0)
        var bitMatch = Regex.Match(workExpression, @"^CONVERT\s*\(\s*\[?bit\]?\s*,\s*\(?\s*([01])\s*\)?\s*\)$", RegexOptions.IgnoreCase);
        if (bitMatch.Success)
        {
            return new LiteralNode { Value = bitMatch.Groups[1].Value == "1" };
        }

        // String: N'some string'
        var stringMatch = Regex.Match(workExpression, @"^N?'(.*?)'$");
        if (stringMatch.Success)
        {
            // The value is the inner part of the quotes.
            return new LiteralNode { Value = stringMatch.Groups[1].Value };
        }

        // Numeric: -123.45
        if (decimal.TryParse(workExpression, out var numericValue))
        {
            return new LiteralNode { Value = numericValue };
        }

        // --- Logical Operators ---
        var andMatch = Regex.Match(workExpression, @"^(.*)\s+AND\s+(.*)$", RegexOptions.IgnoreCase);
        if (andMatch.Success)
        {
            ExpressionNode left = Parse(andMatch.Groups[1].Value.Trim());
            ExpressionNode right = Parse(andMatch.Groups[2].Value.Trim());
            return new OperatorNode
            {
                Left = left,
                Operator = "AND",
                Right = right
            };
        }

        // --- Binary Operators ---
        // e.g., [StandardCost] >= (0.00)
        // This simplified regex captures identifier, operator, and a value on the right.
        var binaryOpMatch = Regex.Match(workExpression, @"^\s*\[([^\]]+)\]\s*([><=!]+)\s*(.*)\s*$", RegexOptions.IgnoreCase);
        if (binaryOpMatch.Success)
        {
            string rightPart = binaryOpMatch.Groups[3].Value;
            // Recursively parse the right side of the expression.
            ExpressionNode rightNode = Parse(rightPart);

            return new OperatorNode
            {
                Left = new IdentifierNode { Name = binaryOpMatch.Groups[1].Value },
                Operator = binaryOpMatch.Groups[2].Value,
                Right = rightNode
            };
        }

        // --- Identifiers ---
        var identifierMatch = Regex.Match(workExpression, @"^\s*\[([^\]]+)\]\s*$");
        if (identifierMatch.Success)
        {
            return new IdentifierNode { Name = identifierMatch.Groups[1].Value };
        }

        // --- Fallback ---
        // If no other pattern matches, treat the expression as a raw literal.
        // This ensures the migration doesn't crash on an unknown expression,
        // though the generated SQL might need manual correction.
        return new LiteralNode { Value = expression };
    }

    /// <summary>
    /// Removes nested outer parentheses from a SQL expression string.
    /// e.g., "((('MyValue')))" becomes "'MyValue'".
    /// </summary>
    private string SanitizeExpression(string expression)
    {
        string result = expression.Trim();
        while (result.StartsWith("(") && result.EndsWith(")"))
        {
            // Remove one layer of parentheses and trim any whitespace.
            result = result.Substring(1, result.Length - 2).Trim();
        }
        return result;
    }
}