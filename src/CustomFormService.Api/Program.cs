using System.Text.Json.Serialization;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => ApiErrors.InvalidModelState(context.ModelState);
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<PasswordCredentialService>();
builder.Services
    .AddOptions<PasswordHasherOptions>()
    .BindConfiguration("PasswordHashing")
    .Validate(options => options.IterationCount >= 100_000, "Password hash iteration count must be at least 100000.")
    .ValidateOnStart();
builder.Services.AddV1Authentication(builder.Configuration, builder.Environment);
builder.Services.AddV1Cors(builder.Configuration);
builder.Services
    .AddOptions<PostgreSqlOptions>()
    .BindConfiguration(PostgreSqlOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "PostgreSQL host must be configured.")
    .Validate(options => options.Port is > 0 and <= 65535, "PostgreSQL port must be between 1 and 65535.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Database), "PostgreSQL database must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "PostgreSQL username must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "PostgreSQL password must be configured.")
    .Validate(
        options => !string.Equals(options.Password, "replace-with-a-local-password", StringComparison.Ordinal),
        "The example PostgreSQL password must be replaced before the API starts.")
    .ValidateOnStart();
builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var postgres = serviceProvider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
    options.UseNpgsql(postgres.BuildConnectionString(), PostgreSqlEnumMappings.Configure);
    options.AddInterceptors(serviceProvider.GetServices<IInterceptor>());
});
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("postgresql", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

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

app.UseCors(V1Cors.PolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
