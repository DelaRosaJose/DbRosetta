/// <summary>
/// Sorts database schema objects based on their dependencies.
/// </summary>
public class SchemaSorter
{
    public List<TableSchema> Sort(List<TableSchema> tables)
    {
        var sorted = new List<TableSchema>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableMap = tables.ToDictionary(t => t.TableName, StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            if (!visited.Contains(table.TableName))
            {
                Visit(table, tableMap, visited, sorted, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }
        return sorted;
    }

    private void Visit(TableSchema table, Dictionary<string, TableSchema> tableMap, HashSet<string> visited, List<TableSchema> sorted, HashSet<string> visiting)
    {
        // Add to visiting set to detect circular dependencies
        if (visiting.Contains(table.TableName))
        {
            // In a real-world scenario, you might want to log this or handle it more gracefully.
            // For now, we'll throw an exception as it's an unresolvable schema issue.
            throw new Exception($"Circular dependency detected involving table '{table.TableName}'.");
        }
        visiting.Add(table.TableName);

        // Recursively visit all dependencies (parent tables) first
        foreach (var fk in table.ForeignKeys)
        {
            // Only visit if the dependency is within the set of tables being migrated
            if (tableMap.TryGetValue(fk.ForeignTable, out var dependency) && !visited.Contains(dependency.TableName))
            {
                Visit(dependency, tableMap, visited, sorted, visiting);
            }
        }

        // All dependencies have been visited, now we can add the current table
        visiting.Remove(table.TableName);
        if (!visited.Contains(table.TableName))
        {
            visited.Add(table.TableName);
            sorted.Add(table);
        }
    }
}