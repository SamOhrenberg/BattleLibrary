using BattleLibrary.Infrastructure;
using BattleLibrary.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Managed hosts (Railway) inject the listen port via PORT; honor it when set.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

const string FrontendCorsPolicy = "frontend";

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    // UI and API are separate origins; must match the UI origin in production.
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors(FrontendCorsPolicy);

if (app.Environment.IsDevelopment())
{
    // OpenAPI doc at /openapi/v1.json, interactive reference at /scalar/v1.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Liveness check.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "battle-library-api",
    timeUtc = DateTimeOffset.UtcNow,
}));

// Readiness check: can the database be reached?
app.MapGet("/api/health/db", async (BattleLibraryDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect
        ? Results.Ok(new { status = "ok", database = "connected" })
        : Results.Problem("Cannot reach the database.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

// Sample endpoint the frontend renders to prove the round-trip.
app.MapGet("/api/game-systems", () => Results.Ok(new[]
{
    new GameSystemSummary("wh40k-10e", "Warhammer 40,000 (10th Edition)", "planned"),
    new GameSystemSummary("kill-team", "Kill Team", "planned"),
}));

app.Run();

internal record GameSystemSummary(string Id, string Name, string Status);
