using DbRosetta.Core; // <-- You already added the reference, so this works

/// <summary>
/// Implements the progress handler contract by writing messages to the console.
/// </summary>
public class ConsoleProgressHandler : IMigrationProgressHandler
{
    public Task SendLogAsync(string message)
    {
        Console.WriteLine(message);
        return Task.CompletedTask;
    }

    public Task SendWarningAsync(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task SendProgressAsync(string tableName, int rows)
    {
        // Use carriage return to create the "live update" effect on a single line
        Console.Write($"\r  -> Migrating {tableName}: {rows} rows transferred...");
        return Task.CompletedTask;
    }

    public Task SendSuccessAsync(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task SendFailureAsync(string errorMessage, string? stackTrace = null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n❌ Migration Failed: {errorMessage}");
        // Optionally print the stack trace for debugging
        // Console.WriteLine(stackTrace); 
        Console.ResetColor();
        return Task.CompletedTask;
    }
}