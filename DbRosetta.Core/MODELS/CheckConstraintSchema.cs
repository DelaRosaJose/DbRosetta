public class CheckConstraintSchema
{
    public string ConstraintName { get; set; } = string.Empty;

    // --- MODIFIED ---
    /// <summary>
    /// The original, unprocessed check clause string from the source database.
    /// </summary>
    public string CheckClauseAsString { get; set; } = string.Empty;

    /// <summary>
    /// The parsed, universal Abstract Syntax Tree for the check clause.
    /// </summary>
    public ExpressionNode? CheckClauseAst { get; set; }
}