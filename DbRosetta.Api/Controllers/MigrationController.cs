using DbRosetta.Api.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly IHubContext<MigrationHub> _hubContext;

    // Inject the SignalR hub for real-time logging
    public MigrationController(IHubContext<MigrationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    [HttpPost("start")]
    public IActionResult StartMigration([FromBody] MigrationRequest request) // Receives the same request object!
    {
        Task.Run(async () =>
        {
            // 1. Create the same service
            var migrationService = new MigrationService();

            try
            {
                // 2. Call the same method, but tell it to log progress by sending a SignalR message
                await migrationService.ExecuteAsync(request, message =>
                {
                    // Send the log message to all connected Flutter clients
                    _hubContext.Clients.All.SendAsync("ReceiveLog", message);
                });
            }
            catch (Exception ex)
            {
                // Send a final failure message
                _hubContext.Clients.All.SendAsync("MigrationFailed", ex.Message);
            }
        });

        return Accepted(); // Immediately tell the UI the job has started
    }
}