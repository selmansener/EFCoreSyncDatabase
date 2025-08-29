using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Ensure data directory exists for SQLite files
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

var sourceDbPath = Path.Combine(dataDir, "source.db");
var targetDbPath = Path.Combine(dataDir, "target.db");
var mappingsDbPath = Path.Combine(dataDir, "mappings.db");

builder.Services.AddDbContext<SourceDbContext>(options =>
    options.UseSqlite($"Data Source={sourceDbPath}"));

builder.Services.AddDbContext<TargetDbContext>(options =>
    options.UseSqlite($"Data Source={targetDbPath}"));

builder.Services.AddDbContext<EntityMappingsDbContext>(options =>
    options.UseSqlite($"Data Source={mappingsDbPath}"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Seed endpoint: resets and repopulates all three databases
app.MapPost("/seed", async (IServiceProvider sp) =>
{
    var result = await SeedService.ResetAndSeedAsync(sp);
    return Results.Ok(result);
})
.WithName("SeedDatabases");

app.Run();

// WeatherForecast record removed
