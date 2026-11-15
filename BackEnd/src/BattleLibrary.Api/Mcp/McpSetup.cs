using System.Security.Claims;
using System.Text;
using BattleLibrary.Api.Auth;
using BattleLibrary.Api.OAuth;
using BattleLibrary.Domain.OAuth;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;

namespace BattleLibrary.Api.Mcp;

public static class McpSetup
{
    public const string Path = "/mcp";
    public const string Scheme = "McpBearer";
    public const string Policy = "mcp";

    private const string Instructions =
        "The Battle Library is a tabletop wargaming companion (Warhammer 40,000 and Kill Team). " +
        "These read-only tools return the signed-in player's army lists, logged games, live game sessions, " +
        "and friends. Use list_* tools to find ids, then get_* tools for detail. Army lists are freeform: " +
        "every unit field is text the player typed, with no rules data behind it.";

    public static void AddBattleLibraryMcp(this IServiceCollection services, AuthenticationBuilder auth, JwtOptions jwt)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "battle-library", Title = "The Battle Library", Version = "1.0.0" };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<BattleLibraryTools>();

        auth.AddJwtBearer(Scheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = OAuthOptions.McpAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                // 401s carry the protected-resource metadata pointer from the MCP handler.
                options.ForwardChallenge = McpAuthenticationDefaults.AuthenticationScheme;
                options.Events = new JwtBearerEvents { OnTokenValidated = RequireLiveGrant };
            })
            .AddMcp(options =>
            {
                options.Events.OnResourceMetadataRequest = context =>
                {
                    var http = context.HttpContext;
                    var issuer = OAuthEndpoints.Issuer(http, http.RequestServices.GetRequiredService<IOptions<OAuthOptions>>().Value);
                    context.ResourceMetadata = new ProtectedResourceMetadata
                    {
                        Resource = $"{issuer}{Path}",
                        AuthorizationServers = [issuer],
                        ScopesSupported = [OAuthRules.McpReadScope],
                        BearerMethodsSupported = ["header"],
                        ResourceName = "The Battle Library",
                    };
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim("scope", OAuthRules.McpReadScope));
    }

    public static void MapBattleLibraryMcp(this WebApplication app) =>
        app.MapMcp(Path)
            .RequireAuthorization(Policy)
            .RequireCors(OAuthEndpoints.PublicCorsPolicy);

    // Disconnecting an app in the UI revokes its grant; access tokens stop working at once.
    private static async Task RequireLiveGrant(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue("grant_id"), out var grantId)
            || !Guid.TryParse(principal?.FindFirstValue("sub"), out var userId))
        {
            context.Fail("Token is missing its grant.");
            return;
        }
        var db = context.HttpContext.RequestServices.GetRequiredService<BattleLibraryDbContext>();
        var live = await db.OAuthGrants.AnyAsync(
            g => g.Id == grantId && g.UserId == userId && g.RevokedAt == null, context.HttpContext.RequestAborted);
        if (!live)
        {
            context.Fail("Grant was revoked.");
        }
    }
}
