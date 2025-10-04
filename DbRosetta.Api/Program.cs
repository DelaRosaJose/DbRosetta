// Add this using statement at the top of the file
using DbRosetta.Api.Hubs;
using DbRosetta.Core.Interfaces;
using DbRosetta.Core.Services;
SQLitePCL.Batteries.Init();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// 1. ---> ADD THIS: Register SignalR's services with the dependency injection container.
builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add logging
builder.Services.AddLogging();

// Register DbRosetta.Core services
builder.Services.AddSingleton<DatabaseProviderFactory>();
builder.Services.AddTransient<IDataMigrator, DataMigratorService>();
builder.Services.AddTransient<MigrationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection(); 
app.UseWebSockets();
app.UseAuthorization();
app.MapControllers();

// 2. ---> ADD THIS: Map the hub to a URL endpoint.
// This tells the server that WebSocket connections to "/migrationHub" should be handled by MigrationHub.
app.MapHub<MigrationHub>("/migrationHub");

app.Run();