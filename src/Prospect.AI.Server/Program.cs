using Prospect.AI.Infrastructure;
using Prospect.AI.Server.Authentication;
using Prospect.AI.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisClientBuilder("cache")
    .WithOutputCache();
builder.AddInfrastructure();
builder.AddProspectAuthentication();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await app.MigrateDatabaseAsync();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache();

var api = app.MapGroup("/api");
api.MapAccountEndpoints();
api.MapAutomationEndpoints();

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
