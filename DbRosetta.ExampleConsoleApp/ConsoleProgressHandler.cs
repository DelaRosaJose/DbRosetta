using DbRosetta.Core;
using System;
using System.Threading.Tasks;

/// <summary>
/// A concrete implementation of the progress handler interface that writes
/// all migration status updates directly to the console window.
/// </summary>
public class ConsoleProgressHandler : IMigrationProgressHandler
{
    private static readonly object _lock = new object();

    public Task SendLogAsync(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }
        return Task.CompletedTask;
    }

    public Task SendWarningAsync(string message)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"? WARNING: {message}");
            Console.ResetColor();
        }
        return Task.CompletedTask;
    }

    public Task SendProgressAsync(string tableName, int rows)
    {
        // This uses a carriage return to show progress on a single line
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  -> Migrating {tableName}: {rows} rows transferred...\r");
            Console.ResetColor();
        }
        return Task.CompletedTask;
    }

    public Task SendSuccessAsync(string message)
    {
        lock (_lock)
        {
            Console.WriteLine(); // New line after progress updates
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }
        return Task.CompletedTask;
    }

    public Task SendFailureAsync(string errorMessage, string? stackTrace = null)
    {
        lock (_lock)
        {
            Console.WriteLine(); // New line after any progress updates
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"? MIGRATION FAILED: {errorMessage}");
            Console.ResetColor();

            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("\n--- Stack Trace ---");
                Console.WriteLine(stackTrace);
                Console.ResetColor();
            }
        }
        return Task.CompletedTask;
    }
}