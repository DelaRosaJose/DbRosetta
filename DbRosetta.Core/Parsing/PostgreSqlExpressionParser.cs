using System.Text.RegularExpressions;

/// <summary>
/// Parses PostgreSQL expression strings into the universal ExpressionNode AST.
/// It handles PostgreSQL-specific syntax like ::text type casts, now(),
/// and the CHECK(...) wrapper returned by system functions.
/// </summary>
public class PostgreSqlExpressionParser : IExpressionParser
{
    public ExpressionNode Parse(string expression)
    {
        string workExpression = expression.Trim();

        // --- Handle CHECK constraints ---
        // pg_get_constraintdef() returns a string like "CHECK ((col > 0))".
        // We need to unwrap this and parse the inner expression.
        var checkMatch = Regex.Match(workExpression, @"^\s*CHECK\s*\((.*)\)\s*$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (checkMatch.Success)
        {
            return Parse(checkMatch.Groups[1].Value);
        }

        // --- Function Calls ---
        if (workExpression.Equals("now()", StringComparison.OrdinalIgnoreCase))
        {
            return new FunctionCallNode { UniversalFunctionName = "GetCurrentTimestamp" };
        }
        if (workExpression.Equals("gen_random_uuid()", StringComparison.OrdinalIgnoreCase))
        {
            return new FunctionCallNode { UniversalFunctionName = "GenerateUuid" };
        }

        // --- Literals ---
        // Boolean
        if (workExpression.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralNode { Value = true };
        }
        if (workExpression.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralNode { Value = false };
        }

        // String with type-cast: 'MyText'::character varying
        var stringCastMatch = Regex.Match(workExpression, @"^'(.*?)'::[\w\s]+");
        if (stringCastMatch.Success)
        {
            return new LiteralNode { Value = stringCastMatch.Groups[1].Value };
        }

        // --- Binary Operators ---
        // e.g., ("ShipBase" > 0.00)
        var binaryOpMatch = Regex.Match(workExpression, @"^\(?""\b*([^""]+)\b*""\s*([><=!]+)\s*(\d+(\.\d+)?)\)?$");
        if (binaryOpMatch.Success)
        {
            return new OperatorNode
            {
                Left = new IdentifierNode { Name = binaryOpMatch.Groups[1].Value },
                Operator = binaryOpMatch.Groups[2].Value,
                Right = new LiteralNode { Value = decimal.Parse(binaryOpMatch.Groups[3].Value) }
            };
        }

        // --- Fallback ---
        // If nothing else matches, treat as a raw literal value.
        return new LiteralNode { Value = workExpression };
    }
}