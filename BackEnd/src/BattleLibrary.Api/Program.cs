using System.Text;
using BattleLibrary.Api.Admin;
using BattleLibrary.Api.Auth;
using BattleLibrary.Api.Feedback;
using BattleLibrary.Api.Games;
using BattleLibrary.Api.Hosting;
using BattleLibrary.Api.Lists;
using BattleLibrary.Api.Mcp;
using BattleLibrary.Api.OAuth;
using BattleLibrary.Api.Social;
using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure;
using BattleLibrary.Infrastructure.Feedback;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Some hosts inject the listen port via PORT; honor it when set.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

const string FrontendCorsPolicy = "frontend";

builder.AddStructuredLogging();
builder.Services.AddErrorHandling();
builder.Services.AddApiRateLimiting();
builder.Services.AddForwardedHeaders();

builder.Services.AddInfrastructure(builder.Configuration);

var feedbackOptions = builder.Configuration.GetSection(FeedbackOptions.SectionName).Get<FeedbackOptions>() ?? new FeedbackOptions();
builder.Services.AddSingleton(feedbackOptions);
builder.Services.AddHttpClient<GitHubIssuesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BattleLibrary/0.1");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    if (feedbackOptions.Enabled)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", feedbackOptions.GitHubToken);
    }
});

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    // UI and API are separate origins; must match the UI origin in production.
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());

    // OAuth and MCP are called by third-party clients from any origin, with bearer tokens only.
    options.AddPolicy(OAuthEndpoints.PublicCorsPolicy, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("WWW-Authenticate", "Mcp-Session-Id", "Mcp-Protocol-Version"));
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection(OAuthOptions.SectionName));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
authentication
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // WebSocket clients can't send an Authorization header; the SignalR hub
        // passes the access token as a query-string parameter instead.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/game"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddBattleLibraryMcp(authentication, jwt);

var app = builder.Build();
var bundledFrontend = app.Environment.HasBundledFrontend();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (bundledFrontend)
{
    app.UseBundledFrontend();
}
app.UseApiRequestLogging();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

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
}).DisableRateLimiting();

// Sample endpoint the frontend renders to prove the round-trip.
app.MapGet("/api/game-systems", () => Results.Ok(new[]
{
    new GameSystemSummary(GameSystems.Warhammer40k, "Warhammer 40,000"),
    new GameSystemSummary(GameSystems.KillTeam, "Kill Team"),
    new GameSystemSummary(GameSystems.Other, "Other"),
}));
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapListEndpoints();
app.MapPresetEndpoints();
app.MapSocialEndpoints();
app.MapGameEndpoints();
app.MapGameSessionEndpoints();
app.MapFeedbackEndpoints();
app.MapOAuthEndpoints();
app.MapBattleLibraryMcp();
app.MapHub<GameHub>("/hubs/game");
if (bundledFrontend)
{
    app.MapBundledFrontend();
}

// Ensure the settings row exists and grant admin to configured seed emails.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BattleLibraryDbContext>();
    if (!app.Environment.IsDevelopment())
    {
        await db.Database.MigrateAsync();
    }
    try
    {
        await AdminEndpoints.SeedAsync(db, app.Configuration);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Access seeding skipped (database not ready?).");
    }
}

app.Run();

internal record GameSystemSummary(string Id, string Name);
