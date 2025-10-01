using DbRosetta.Api.Hubs;
using DbRosetta.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly IHubContext<MigrationHub> _hubContext;

    public MigrationController(IHubContext<MigrationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    [HttpPost("start")]
    public IActionResult StartMigration([FromBody] MigrationRequest request)
    {
        // --- THIS IS THE FIX ---
        // We run the entire operation, including service creation and the try/catch,
        // inside the background task.
        _ = Task.Run(async () =>
        {
            // Create the handler that knows how to talk to the client
            var progressHandler = new SignalRProgressHandler(_hubContext);

            // Create the core migration engine
            var migrationService = new MigrationService(progressHandler);

            try
            {
                // Await the entire migration process
                await migrationService.ExecuteAsync(request);
            }
            catch (Exception ex)
            {
                // If ExecuteAsync throws an unhandled exception, this will catch it.
                // The service itself should have already sent a more specific
                // failure message, but this is a final safety net.
                Console.WriteLine($"--- UNHANDLED MIGRATION EXCEPTION: {ex} ---");

                // Send a generic failure message to the client so the UI can unlock.
                await progressHandler.SendFailureAsync("An unexpected error occurred in the migration engine.", ex.ToString());
            }
        });

        return Accepted(new { Message = "Migration job accepted and started." });
    }
}