namespace DbRosetta.Core.Models
{
    /// <summary>
    /// Represents a single row of data in a universal format, independent of the database type.
    /// Uses a dictionary to map column names to their values.
    /// </summary>
    public class UniversalDataRow
    {
        public Dictionary<string, object?> Data { get; set; } = new Dictionary<string, object?>();

        public UniversalDataRow() { }

        public UniversalDataRow(Dictionary<string, object?> data)
        {
            Data = data ?? new Dictionary<string, object?>();
        }

        /// <summary>
        /// Gets the value for a specific column.
        /// </summary>
        public object? GetValue(string columnName)
        {
            return Data.TryGetValue(columnName, out var value) ? value : null;
        }

        /// <summary>
        /// Sets the value for a specific column.
        /// </summary>
        public void SetValue(string columnName, object? value)
        {
            Data[columnName] = value;
        }
    }
}