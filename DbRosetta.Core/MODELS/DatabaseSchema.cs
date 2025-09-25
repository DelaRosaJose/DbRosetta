public class DatabaseSchema
{
    public List<TableSchema> Tables { get; set; } = new();
    public List<ViewSchema> Views { get; set; } = new();
}