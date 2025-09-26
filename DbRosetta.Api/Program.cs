// --- In DbRosetta.Api/Program.cs ---

// Add this using statement at the top
using DbRosetta.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// 1. ---> ADD THIS LINE to register SignalR's services for dependency injection.
builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// 2. ---> ADD THIS LINE to map the hub to a specific URL endpoint.
// This tells the server that WebSocket requests to "/migrationHub" should be handled by MigrationHub.
app.MapHub<MigrationHub>("/migrationHub");

app.Run();