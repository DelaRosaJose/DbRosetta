public class IndexSchema
{
    public string IndexName { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public List<IndexColumn> Columns { get; set; } = new();
}

public class IndexColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public bool IsAscending { get; set; }
}