// Location: DbRosetta.Api/Hubs/SignalRProgressHandler.cs

using DbRosetta.Core; // <-- Add reference to your Core project
using Microsoft.AspNetCore.SignalR;

namespace DbRosetta.Api.Hubs
{
    /// <summary>
    /// Implements the progress handler contract by sending messages over SignalR.
    /// This class acts as the "adapter" between the core engine and the real-time web layer.
    /// </summary>
    public class SignalRProgressHandler : IMigrationProgressHandler
    {
        private readonly IHubContext<MigrationHub> _hubContext;

        public SignalRProgressHandler(IHubContext<MigrationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task SendLogAsync(string message) =>
            _hubContext.Clients.All.SendAsync("ReceiveLog", message);

        public Task SendWarningAsync(string message) =>
            _hubContext.Clients.All.SendAsync("ReceiveWarning", message);

        public Task SendProgressAsync(string tableName, int rows) =>
            _hubContext.Clients.All.SendAsync("ReceiveProgress", new { tableName, rows });

        public Task SendSuccessAsync(string message) =>
            _hubContext.Clients.All.SendAsync("MigrationSuccess", message);

        public Task SendFailureAsync(string errorMessage, string? stackTrace = null) =>
            _hubContext.Clients.All.SendAsync("MigrationFailed", new { errorMessage, stackTrace });
    }
}