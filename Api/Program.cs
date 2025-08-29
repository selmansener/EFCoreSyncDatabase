using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure SQL Server connections
builder.Services.AddDbContext<SourceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SourceDb"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddDbContext<TargetDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TargetDb"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddDbContext<EntityMappingsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MappingsDb"),
        sql => sql.EnableRetryOnFailure()));

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
