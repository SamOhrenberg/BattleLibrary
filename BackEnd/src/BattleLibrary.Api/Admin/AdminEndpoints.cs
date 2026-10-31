using System.Security.Claims;
using System.Security.Cryptography;
using BattleLibrary.Api.Auth;
using BattleLibrary.Domain.Accounts;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Admin;

// Site administration (JWT + the admin flag on the user), distinct from the
// catalogue admin endpoints which use a shared X-Admin-Key.
public static class AdminEndpoints
{
    // Emails that become admin on sign-up and can register even when closed.
    // Configured via Admin:SeedEmails (array); set per environment, not in code.
    public static IReadOnlySet<string> SeedEmails(IConfiguration config) =>
        (config.GetSection("Admin:SeedEmails").Get<string[]>() ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin").RequireAuthorization().AddEndpointFilter<AdminUserFilter>();
        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", UpdateSettings);
        group.MapGet("/invites", GetInvites);
        group.MapPost("/invites", CreateInvite);
        group.MapDelete("/invites/{id:guid}", RevokeInvite);
    }

    private static async Task<IResult> GetSettings(BattleLibraryDbContext db, CancellationToken ct)
    {
        var open = await db.AppSettings.Select(s => s.RegistrationOpen).FirstOrDefaultAsync(ct);
        return Results.Ok(new AdminSettingsDto(open));
    }

    private static async Task<IResult> UpdateSettings(UpdateSettingsRequest request, BattleLibraryDbContext db, CancellationToken ct)
    {
        var settings = await db.AppSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AppSetting { Id = 1 };
            db.AppSettings.Add(settings);
        }
        settings.RegistrationOpen = request.RegistrationOpen;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new AdminSettingsDto(settings.RegistrationOpen));
    }

    private static async Task<IResult> GetInvites(BattleLibraryDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var invites = await db.Invites.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return Results.Ok(invites.Select(i => ToDto(i, now)));
    }

    private static async Task<IResult> CreateInvite(
        CreateInviteRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        var userId = Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
        var email = request.Email?.Trim();
        if (email is { Length: > 0 } && !email.Contains('@'))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Enter a valid email, or leave it blank for a shareable link."],
            });
        }

        var invite = new Invite
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            MaxUses = request.MaxUses is { } m && m >= 0 ? m : 1,
            ExpiresAt = request.ExpiresInDays is { } d && d > 0 ? DateTimeOffset.UtcNow.AddDays(d) : null,
            CreatedByUserId = userId,
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(invite, DateTimeOffset.UtcNow));
    }

    private static async Task<IResult> RevokeInvite(Guid id, BattleLibraryDbContext db, CancellationToken ct)
    {
        var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invite is null)
        {
            return Results.NotFound();
        }
        invite.Revoked = true;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static InviteDto ToDto(Invite i, DateTimeOffset now) =>
        new(i.Id, i.Token, i.Email, i.ExpiresAt, i.MaxUses, i.UseCount, i.Revoked, i.IsUsable(now), i.CreatedAt);

    // Idempotent startup seed: ensure the settings row exists and grant admin to
    // any configured seed emails that have already registered.
    public static async Task SeedAsync(BattleLibraryDbContext db, IConfiguration config, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct))
        {
            return;
        }
        if (!await db.AppSettings.AnyAsync(ct))
        {
            db.AppSettings.Add(new AppSetting { Id = 1, RegistrationOpen = false });
        }
        var seeds = SeedEmails(config);
        if (seeds.Count > 0)
        {
            var users = await db.Users.Where(u => !u.IsAdmin).ToListAsync(ct);
            foreach (var u in users.Where(u => seeds.Contains(u.Email)))
            {
                u.IsAdmin = true;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

// Authorizes site-admin endpoints by the DB admin flag (authoritative, so a
// revoked admin loses access immediately rather than waiting for token expiry).
public sealed class AdminUserFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(http.User.FindFirst("sub")?.Value, out var userId))
        {
            return Results.Unauthorized();
        }
        var db = http.RequestServices.GetRequiredService<BattleLibraryDbContext>();
        var isAdmin = await db.Users.AnyAsync(u => u.Id == userId && u.IsAdmin);
        return isAdmin ? await next(context) : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
