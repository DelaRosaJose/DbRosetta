public enum TriggerEvent
{
    Delete,
    Update,
    Insert
}

public enum TriggerType
{
    After,
    Before
}

public class TriggerSchema
{
    public string Name { get; set; } = string.Empty;
    public TriggerEvent Event { get; set; }
    public TriggerType Type { get; set; }
    public string Body { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
}