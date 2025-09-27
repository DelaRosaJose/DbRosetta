// Location: DbRosetta.Core/IMigrationProgressHandler.cs

namespace DbRosetta.Core
{
    /// <summary>
    /// Defines a contract for reporting migration progress.
    /// This allows the core service to be decoupled from the implementation (e.g., SignalR, Console).
    /// </summary>
    public interface IMigrationProgressHandler
    {
        Task SendLogAsync(string message);
        Task SendWarningAsync(string message);
        Task SendProgressAsync(string tableName, int rows);
        Task SendSuccessAsync(string message);
        Task SendFailureAsync(string errorMessage, string? stackTrace = null);
    }
}