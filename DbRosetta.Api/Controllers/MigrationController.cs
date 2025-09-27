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
        Task.Run(async () =>
        {
            // 1. Create the concrete SignalR handler
            var progressHandler = new SignalRProgressHandler(_hubContext);

            // 2. Create the service and give it the handler
            var migrationService = new MigrationService(progressHandler);

            try
            {
                // 3. The service now runs, completely unaware of SignalR
                await migrationService.ExecuteAsync(request);
            }
            catch
            {
                // The service already sent the failure message via the handler
            }
        });

        return Accepted(new { Message = "Migration job accepted and started." });
    }
}