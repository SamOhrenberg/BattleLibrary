using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using BattleLibrary.Api.Auth;
using BattleLibrary.Api.Hosting;
using BattleLibrary.Domain.OAuth;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BattleLibrary.Api.OAuth;

// OAuth 2.1 authorization server for MCP clients: metadata, dynamic client
// registration, authorization code + PKCE, refresh rotation, and revocation.
// Consent happens in the SPA, which is already signed in with a user token.
public static class OAuthEndpoints
{
    public const string PublicCorsPolicy = "oauth-public";

    private const int MaxRedirectUris = 10;

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/.well-known/oauth-authorization-server", Metadata).RequireCors(PublicCorsPolicy);

        var oauth = routes.MapGroup("/oauth")
            .RequireCors(PublicCorsPolicy)
            .RequireRateLimiting(HostingExtensions.OAuthPolicy)
            .DisableAntiforgery();
        oauth.MapPost("/register", Register);
        oauth.MapGet("/authorize", Authorize);
        oauth.MapPost("/token", Token);
        oauth.MapPost("/revoke", Revoke);

        var api = routes.MapGroup("/api/oauth").RequireAuthorization();
        api.MapGet("/consent", ConsentInfo);
        api.MapPost("/consent", Consent);
        api.MapGet("/apps", ConnectedApps);
        api.MapDelete("/apps/{grantId:guid}", Disconnect);
    }

    internal static string Issuer(HttpContext http, OAuthOptions options) =>
        !string.IsNullOrWhiteSpace(options.PublicUrl)
            ? options.PublicUrl.TrimEnd('/')
            : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}".TrimEnd('/');

    private static IResult Metadata(HttpContext http, IOptions<OAuthOptions> options)
    {
        var issuer = Issuer(http, options.Value);
        return Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = $"{issuer}/oauth/authorize",
            ["token_endpoint"] = $"{issuer}/oauth/token",
            ["registration_endpoint"] = $"{issuer}/oauth/register",
            ["revocation_endpoint"] = $"{issuer}/oauth/revoke",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["revocation_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = new[] { OAuthRules.McpReadScope },
            ["authorization_response_iss_parameter_supported"] = true,
        });
    }

    // RFC 7591. Every client is public (PKCE, no secret), whatever auth method it asks for.
    private static async Task<IResult> Register(
        RegisterClientRequest request, BattleLibraryDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        var uris = request.RedirectUris?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList() ?? [];
        if (uris.Count is 0 or > MaxRedirectUris)
        {
            return RegistrationError("invalid_redirect_uri", $"Provide 1-{MaxRedirectUris} redirect_uris.");
        }
        if (uris.FirstOrDefault(u => !OAuthRules.IsAllowedRedirectUri(u)) is { } bad)
        {
            return RegistrationError("invalid_redirect_uri", $"Redirect URI not allowed: {bad}");
        }

        var name = request.ClientName?.Trim();
        var client = new OAuthClient
        {
            ClientName = string.IsNullOrEmpty(name) ? "MCP client" : name.Length > 200 ? name[..200] : name,
            RedirectUris = uris,
        };
        db.OAuthClients.Add(client);
        await db.SaveChangesAsync(ct);
        loggers.CreateLogger("OAuth").LogInformation("OAuth client registered: {ClientName} ({ClientId})", client.ClientName, client.PublicId);

        return Results.Json(new Dictionary<string, object>
        {
            ["client_id"] = client.PublicId,
            ["client_id_issued_at"] = client.CreatedAt.ToUnixTimeSeconds(),
            ["client_name"] = client.ClientName,
            ["redirect_uris"] = client.RedirectUris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
        }, statusCode: StatusCodes.Status201Created);
    }

    // Validates the request, then hands off to the SPA's consent screen.
    private static async Task<IResult> Authorize(
        HttpContext http, BattleLibraryDbContext db, IOptions<OAuthOptions> options, CancellationToken ct)
    {
        var q = http.Request.Query;
        var redirectUri = q["redirect_uri"].ToString();
        var client = await FindClientAsync(db, q["client_id"], ct);
        if (client is null || !OAuthRules.RedirectMatches(client.RedirectUris, redirectUri))
        {
            // Never redirect to an unverified URI.
            return Results.Text("Invalid authorization request: unknown client or redirect URI.", "text/plain", statusCode: 400);
        }

        var issuer = Issuer(http, options.Value);
        string? state = q["state"];
        if (q["response_type"] != "code")
        {
            return Results.Redirect(ErrorRedirect(redirectUri, "unsupported_response_type", "Only response_type=code is supported.", state, issuer));
        }
        if (q["code_challenge_method"] != "S256" || !OAuthRules.IsValidChallenge(q["code_challenge"]))
        {
            return Results.Redirect(ErrorRedirect(redirectUri, "invalid_request", "PKCE with S256 is required.", state, issuer));
        }
        if (string.IsNullOrWhiteSpace(options.Value.FrontendUrl))
        {
            return Results.Text("Sign-in for connected apps is not configured on this server.", "text/plain", statusCode: 503);
        }

        return Results.Redirect(OAuthRules.AppendQuery(options.Value.FrontendUrl,
        [
            new("oauth_authorize", "1"),
            new("client_id", client.PublicId),
            new("redirect_uri", redirectUri),
            new("code_challenge", q["code_challenge"]),
            new("state", state),
        ]));
    }

    private static async Task<IResult> ConsentInfo(
        string? clientId, string? redirectUri, BattleLibraryDbContext db, CancellationToken ct)
    {
        var client = await FindClientAsync(db, clientId, ct);
        if (client is null || !OAuthRules.RedirectMatches(client.RedirectUris, redirectUri))
        {
            return Results.BadRequest(new { error = "This connection request is invalid or has expired. Start again from the app you are connecting." });
        }
        return Results.Ok(new ConsentInfoDto(client.ClientName, new Uri(redirectUri!).Host, client.CreatedAt));
    }

    private static async Task<IResult> Consent(
        ConsentRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        BattleLibraryDbContext db,
        IOptions<OAuthOptions> options,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }
        var client = await FindClientAsync(db, request.ClientId, ct);
        if (client is null || !OAuthRules.RedirectMatches(client.RedirectUris, request.RedirectUri))
        {
            return Results.BadRequest(new { error = "This connection request is invalid or has expired." });
        }

        var issuer = Issuer(http, options.Value);
        if (!request.Approve)
        {
            return Results.Ok(new ConsentResultDto(
                ErrorRedirect(request.RedirectUri, "access_denied", "The user declined access.", request.State, issuer)));
        }
        if (!OAuthRules.IsValidChallenge(request.CodeChallenge))
        {
            return Results.Ok(new ConsentResultDto(
                ErrorRedirect(request.RedirectUri, "invalid_request", "PKCE with S256 is required.", request.State, issuer)));
        }

        var now = DateTimeOffset.UtcNow;
        // Drop this user's never-exchanged codes, and replace any earlier grant to the same client.
        await db.OAuthGrants
            .Where(g => g.UserId == userId && g.CodeUsedAt == null && g.CodeExpiresAt < now)
            .ExecuteDeleteAsync(ct);
        await db.OAuthGrants
            .Where(g => g.UserId == userId && g.ClientId == client.Id && g.RevokedAt == null && g.CodeUsedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, now), ct);

        var code = OAuthRules.NewSecret();
        db.OAuthGrants.Add(new OAuthGrant
        {
            UserId = userId,
            ClientId = client.Id,
            Scope = OAuthRules.McpReadScope,
            CodeHash = OAuthRules.Hash(code),
            CodeChallenge = request.CodeChallenge,
            RedirectUri = request.RedirectUri,
            CodeExpiresAt = now.AddMinutes(options.Value.CodeMinutes),
        });
        await db.SaveChangesAsync(ct);
        loggers.CreateLogger("OAuth").LogInformation("OAuth consent granted to {ClientName} ({ClientId})", client.ClientName, client.PublicId);

        return Results.Ok(new ConsentResultDto(OAuthRules.AppendQuery(request.RedirectUri,
        [
            new("code", code),
            new("state", request.State),
            new("iss", issuer),
        ])));
    }

    private static async Task<IResult> Token(
        HttpContext http, BattleLibraryDbContext db, TokenService tokens, IOptions<OAuthOptions> options, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!http.Request.HasFormContentType)
        {
            return TokenError("invalid_request", "Expected an application/x-www-form-urlencoded body.");
        }
        var form = await http.Request.ReadFormAsync(ct);
        var clientId = ClientIdFrom(http, form);
        if (string.IsNullOrEmpty(clientId))
        {
            return TokenError("invalid_client", "client_id is required.", StatusCodes.Status401Unauthorized);
        }

        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await ExchangeCode(form, clientId, db, tokens, options.Value, ct),
            "refresh_token" => await RefreshGrant(form, clientId, db, tokens, options.Value, ct),
            _ => TokenError("unsupported_grant_type", "Use authorization_code or refresh_token."),
        };
    }

    private static async Task<IResult> ExchangeCode(
        IFormCollection form, string clientId, BattleLibraryDbContext db, TokenService tokens, OAuthOptions options, CancellationToken ct)
    {
        var code = form["code"].ToString();
        if (code.Length == 0)
        {
            return TokenError("invalid_request", "code is required.");
        }
        var now = DateTimeOffset.UtcNow;
        var codeHash = OAuthRules.Hash(code);
        var grant = await db.OAuthGrants.AsNoTracking().Include(g => g.User)
            .FirstOrDefaultAsync(g => g.CodeHash == codeHash, ct);
        if (grant is null)
        {
            return TokenError("invalid_grant", "Unknown authorization code.");
        }
        if (grant.CodeUsedAt is not null)
        {
            // A replayed code may be stolen: revoke what it issued.
            await db.OAuthGrants.Where(g => g.Id == grant.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, now), ct);
            return TokenError("invalid_grant", "Authorization code already used.");
        }
        if (grant.RevokedAt is not null || grant.CodeExpiresAt < now
            || grant.ClientId.ToString("N") != clientId
            || form["redirect_uri"].ToString() != grant.RedirectUri
            || !OAuthRules.VerifyPkce(form["code_verifier"], grant.CodeChallenge))
        {
            return TokenError("invalid_grant", "Authorization code is invalid, expired, or does not match this request.");
        }

        var refresh = OAuthRules.NewSecret();
        var claimed = await db.OAuthGrants
            .Where(g => g.Id == grant.Id && g.CodeUsedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.CodeUsedAt, now)
                .SetProperty(g => g.RefreshTokenHash, OAuthRules.Hash(refresh))
                .SetProperty(g => g.RefreshExpiresAt, now.AddDays(options.RefreshTokenDays))
                .SetProperty(g => g.LastUsedAt, now), ct);
        if (claimed == 0)
        {
            return TokenError("invalid_grant", "Authorization code already used.");
        }
        return TokenResponse(grant, clientId, refresh, tokens, options);
    }

    private static async Task<IResult> RefreshGrant(
        IFormCollection form, string clientId, BattleLibraryDbContext db, TokenService tokens, OAuthOptions options, CancellationToken ct)
    {
        var token = form["refresh_token"].ToString();
        if (token.Length == 0)
        {
            return TokenError("invalid_request", "refresh_token is required.");
        }
        var now = DateTimeOffset.UtcNow;
        var hash = OAuthRules.Hash(token);
        var grant = await db.OAuthGrants.AsNoTracking().Include(g => g.User)
            .FirstOrDefaultAsync(g => g.RefreshTokenHash == hash, ct);
        if (grant is null || grant.RevokedAt is not null || grant.RefreshExpiresAt < now
            || grant.ClientId.ToString("N") != clientId)
        {
            return TokenError("invalid_grant", "Refresh token is invalid, expired, or revoked.");
        }

        var next = OAuthRules.NewSecret();
        var rotated = await db.OAuthGrants
            .Where(g => g.Id == grant.Id && g.RefreshTokenHash == hash && g.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.RefreshTokenHash, OAuthRules.Hash(next))
                .SetProperty(g => g.RefreshExpiresAt, now.AddDays(options.RefreshTokenDays))
                .SetProperty(g => g.LastUsedAt, now), ct);
        if (rotated == 0)
        {
            return TokenError("invalid_grant", "Refresh token already used.");
        }
        return TokenResponse(grant, clientId, next, tokens, options);
    }

    private static IResult TokenResponse(OAuthGrant grant, string clientId, string refresh, TokenService tokens, OAuthOptions options)
    {
        var (access, expires) = tokens.CreateMcpAccessToken(grant.User, clientId, grant.Id, grant.Scope, options.AccessTokenMinutes);
        return Results.Json(new Dictionary<string, object>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)(expires - DateTime.UtcNow).TotalSeconds,
            ["refresh_token"] = refresh,
            ["scope"] = grant.Scope,
        });
    }

    // RFC 7009: always 200, whether or not the token was known.
    private static async Task<IResult> Revoke(HttpContext http, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(ct);
            var token = form["token"].ToString();
            if (token.Length > 0)
            {
                var hash = OAuthRules.Hash(token);
                var now = DateTimeOffset.UtcNow;
                await db.OAuthGrants
                    .Where(g => g.RefreshTokenHash == hash && g.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, now), ct);
            }
        }
        return Results.Ok();
    }

    private static async Task<IResult> ConnectedApps(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }
        var apps = await db.OAuthGrants
            .Where(g => g.UserId == userId && g.RevokedAt == null && g.CodeUsedAt != null)
            .OrderByDescending(g => g.LastUsedAt)
            .Select(g => new ConnectedAppDto(g.Id, g.Client.ClientName, g.CodeUsedAt!.Value, g.LastUsedAt))
            .ToListAsync(ct);
        return Results.Ok(apps);
    }

    private static async Task<IResult> Disconnect(
        Guid grantId, ClaimsPrincipal principal, BattleLibraryDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }
        var now = DateTimeOffset.UtcNow;
        var revoked = await db.OAuthGrants
            .Where(g => g.Id == grantId && g.UserId == userId && g.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, now), ct);
        if (revoked == 0)
        {
            return Results.NotFound();
        }
        loggers.CreateLogger("OAuth").LogInformation("OAuth grant {GrantId} disconnected by its user", grantId);
        return Results.NoContent();
    }

    private static async Task<OAuthClient?> FindClientAsync(BattleLibraryDbContext db, string? clientId, CancellationToken ct) =>
        Guid.TryParseExact(clientId, "N", out var id)
            ? await db.OAuthClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            : null;

    // Public clients send client_id in the form; some send it as an HTTP Basic username.
    private static string? ClientIdFrom(HttpContext http, IFormCollection form)
    {
        var fromForm = form["client_id"].ToString();
        if (fromForm.Length > 0)
        {
            return fromForm;
        }
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var user = decoded.Split(':', 2)[0];
            return Uri.UnescapeDataString(user);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string ErrorRedirect(string redirectUri, string error, string description, string? state, string issuer) =>
        OAuthRules.AppendQuery(redirectUri,
        [
            new("error", error),
            new("error_description", description),
            new("state", state),
            new("iss", issuer),
        ]);

    private static IResult TokenError(string error, string description, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: status);

    private static IResult RegistrationError(string error, string description) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: StatusCodes.Status400BadRequest);
}

public sealed record RegisterClientRequest(
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris);

public sealed record ConsentRequest(
    string ClientId, string RedirectUri, string CodeChallenge, string? State, bool Approve);

public sealed record ConsentInfoDto(string ClientName, string RedirectHost, DateTimeOffset RegisteredAt);

public sealed record ConsentResultDto(string RedirectUrl);

public sealed record ConnectedAppDto(Guid Id, string ClientName, DateTimeOffset ConnectedAt, DateTimeOffset? LastUsedAt);
