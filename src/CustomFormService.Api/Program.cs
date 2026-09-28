using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services
    .AddOptions<PostgreSqlOptions>()
    .BindConfiguration(PostgreSqlOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "PostgreSQL host must be configured.")
    .Validate(options => options.Port is > 0 and <= 65535, "PostgreSQL port must be between 1 and 65535.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Database), "PostgreSQL database must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "PostgreSQL username must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "PostgreSQL password must be configured.")
    .ValidateOnStart();
builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var postgres = serviceProvider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
    options.UseNpgsql(postgres.BuildConnectionString());
});
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("postgresql", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Custom Form Service API v1");
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
