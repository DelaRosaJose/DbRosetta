/// <summary>
/// Sorts database schema objects based on their dependencies.
/// </summary>
public class SchemaSorter
{
    /// <summary>
    /// Sorts a list of tables topologically based on their foreign key dependencies.
    /// Tables without dependencies will appear before tables that depend on them.
    /// </summary>
    /// <param name="tables">The unsorted list of tables.</param>
    /// <returns>A sorted list of tables, or the original list if sorting fails (e.g., due to circular dependencies).</returns>
    public List<TableSchema> SortTablesByDependency(List<TableSchema> tables)
    {
        var sorted = new List<TableSchema>();
        var visited = new HashSet<string>();
        var tableDict = tables.ToDictionary(t => t.TableName, t => t, System.StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            if (!visited.Contains(table.TableName))
            {
                if (!Visit(table, sorted, visited, new HashSet<string>(System.StringComparer.OrdinalIgnoreCase), tableDict))
                {
                    // Circular dependency detected, return original list as a fallback
                    System.Console.WriteLine("Warning: A circular dependency between tables was detected. Schema creation may fail.");
                    return tables;
                }
            }
        }

        return sorted;
    }

    private bool Visit(TableSchema table, List<TableSchema> sorted, HashSet<string> visited, HashSet<string> recursionStack, Dictionary<string, TableSchema> tableDict)
    {
        visited.Add(table.TableName);
        recursionStack.Add(table.TableName);

        foreach (var fk in table.ForeignKeys)
        {
            // Only consider dependencies within the list of tables to be migrated
            if (tableDict.TryGetValue(fk.ForeignTableName, out var dependency))
            {
                if (recursionStack.Contains(dependency.TableName))
                {
                    // Circular dependency found
                    return false;
                }
                if (!visited.Contains(dependency.TableName))
                {
                    if (!Visit(dependency, sorted, visited, recursionStack, tableDict))
                    {
                        return false;
                    }
                }
            }
        }

        recursionStack.Remove(table.TableName);
        sorted.Add(table);
        return true;
    }
}