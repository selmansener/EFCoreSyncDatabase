using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

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
    app.MapScalarApiReference(options =>
    {
        options.Title = "EFCore Sync API";
        options.Theme = ScalarTheme.Kepler;
    });
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// WeatherForecast record removed
