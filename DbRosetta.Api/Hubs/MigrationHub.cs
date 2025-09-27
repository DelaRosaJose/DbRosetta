using Microsoft.AspNetCore.SignalR;

namespace DbRosetta.Api.Hubs // Use your API project's namespace
{
    /// <summary>
    /// This hub is the real-time communication endpoint for migration progress.
    /// The Flutter client will connect to this hub to receive live updates.
    /// </summary>
    public class MigrationHub : Hub
    {
        // This class can be empty for now.
        // Its purpose is to provide a strongly-typed context for SignalR.
        // You could add methods here later that the client could call,
        // e.g., public async Task CancelMigration(string jobId) { ... }
    }
}