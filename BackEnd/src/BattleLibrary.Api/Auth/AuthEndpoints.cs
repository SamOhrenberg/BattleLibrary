using System.Security.Claims;
using BattleLibrary.Api.Admin;
using BattleLibrary.Domain.Accounts;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapPost("/register", Register);
        group.MapGet("/registration-status", RegistrationStatus);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        group.MapGet("/me", Me).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> RegistrationStatus(BattleLibraryDbContext db, CancellationToken ct)
    {
        var open = await db.AppSettings.Select(s => s.RegistrationOpen).FirstOrDefaultAsync(ct);
        return Results.Ok(new RegistrationStatusDto(open));
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        BattleLibraryDbContext db,
        IPasswordHasher<User> hasher,
        TokenService tokens,
        IConfiguration config,
        CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;

        if (username.Length is < 3 or > 50)
        {
            return Problem("username", "Username must be 3-50 characters.");
        }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return Problem("email", "A valid email is required.");
        }
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
        {
            return Problem("password", "Password must be at least 8 characters.");
        }

        // Access gate. The seed admin and the very first account can always
        // register and become admin; otherwise registration must be open, or a
        // valid invite (matching the email, if the invite is email-bound) given.
        var isSeed = AdminEndpoints.SeedEmails(config).Contains(email);
        var isFirstUser = !await db.Users.AnyAsync(ct);
        var now = DateTimeOffset.UtcNow;

        Invite? invite = null;
        var allowed = isSeed || isFirstUser;
        if (!allowed)
        {
            allowed = await db.AppSettings.Select(s => s.RegistrationOpen).FirstOrDefaultAsync(ct);
        }
        if (!allowed && !string.IsNullOrWhiteSpace(request.InviteToken))
        {
            invite = await db.Invites.FirstOrDefaultAsync(i => i.Token == request.InviteToken, ct);
            if (invite is not null
                && invite.IsUsable(now)
                && (invite.Email is null || string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                allowed = true;
            }
            else
            {
                return Problem("invite", "This invite link is invalid, expired, or already used.");
            }
        }
        if (!allowed)
        {
            return Problem("registration", "Sign-ups are invite-only right now. Ask for an invite to join.");
        }

        var taken = await db.Users.AnyAsync(u => u.Username == username || u.Email == email, ct);
        if (taken)
        {
            return Results.Conflict(new { error = "That username or email is already taken." });
        }

        var user = new User
        {
            Username = username,
            Email = email,
            IsAdmin = isSeed || isFirstUser,
            Profile = new Profile { Visibility = ProfileVisibility.Private },
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        if (invite is not null)
        {
            invite.UseCount++;
        }

        var response = IssueTokens(db, tokens, user);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        BattleLibraryDbContext db,
        IPasswordHasher<User> hasher,
        TokenService tokens,
        CancellationToken ct)
    {
        var identifier = request.UsernameOrEmail?.Trim() ?? string.Empty;

        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Username == identifier || u.Email == identifier, ct);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
        }

        var response = IssueTokens(db, tokens, user);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        BattleLibraryDbContext db,
        TokenService tokens,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Unauthorized();
        }

        var hash = TokenService.HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive)
        {
            return Results.Unauthorized();
        }

        stored.RevokedAt = DateTimeOffset.UtcNow;
        var response = IssueTokens(db, tokens, stored.User);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(
        RefreshRequest request,
        BattleLibraryDbContext db,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var hash = TokenService.HashToken(request.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored is { RevokedAt: null })
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        BattleLibraryDbContext db,
        CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(ToDto(user));
    }

    private static AuthResponse IssueTokens(BattleLibraryDbContext db, TokenService tokens, User user)
    {
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user);
        var (rawRefresh, refreshHash, refreshExpires) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpires,
        });

        return new AuthResponse(accessToken, accessExpires, rawRefresh, refreshExpires, ToDto(user));
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Username, user.Email, (user.Profile?.Visibility ?? ProfileVisibility.Private).ToString(), user.IsAdmin);

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
