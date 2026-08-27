using System.Security.Claims;
using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Domain.Social;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Social;

public static class SocialEndpoints
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder routes)
    {
        var profile = routes.MapGroup("/api/profile").RequireAuthorization();
        profile.MapGet("", GetProfile);
        profile.MapPut("", UpdateProfile);

        var users = routes.MapGroup("/api/users").RequireAuthorization();
        users.MapGet("/search", SearchUsers);
        users.MapGet("/{username}", GetPublicProfile);

        var friends = routes.MapGroup("/api/friends").RequireAuthorization();
        friends.MapGet("", GetFriends);
        friends.MapGet("/requests", GetRequests);
        friends.MapPost("/requests", SendRequest);
        friends.MapPost("/requests/{id:guid}/accept", AcceptRequest);
        friends.MapDelete("/requests/{id:guid}", CancelRequest);
        friends.MapDelete("/{userId:guid}", RemoveFriend);
    }

    private static async Task<IResult> GetProfile(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == me, ct);
        if (profile is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(new ProfileSettingsDto(profile.Visibility.ToString(), profile.Bio));
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        if (!Enum.TryParse<ProfileVisibility>(request.Visibility, ignoreCase: true, out var visibility))
        {
            return Problem("visibility", "Visibility must be 'Public' or 'Private'.");
        }
        var bio = request.Bio?.Trim();
        if (bio is { Length: > 500 })
        {
            return Problem("bio", "Bio must be 500 characters or fewer.");
        }

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == me, ct);
        if (profile is null)
        {
            return Results.NotFound();
        }
        profile.Visibility = visibility;
        profile.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ProfileSettingsDto(profile.Visibility.ToString(), profile.Bio));
    }

    private static async Task<IResult> SearchUsers(string? q, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < 1)
        {
            return Results.Ok(Array.Empty<UserSearchResultDto>());
        }

        var matches = await db.Users
            .Include(u => u.Profile)
            .Where(u => u.Id != me && EF.Functions.ILike(u.Username, $"%{term}%"))
            .OrderBy(u => u.Username)
            .Take(20)
            .Select(u => new { u.Id, u.Username, u.Profile.Visibility })
            .ToListAsync(ct);

        var ids = matches.Select(m => m.Id).ToList();
        var links = await LinksWith(db, me, ids, ct);

        var results = matches.Select(m => new UserSearchResultDto(
            m.Id, m.Username, m.Visibility.ToString(),
            StatusFor(links.GetValueOrDefault(m.Id), me)));
        return Results.Ok(results);
    }

    private static async Task<IResult> GetPublicProfile(string username, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        var link = await LinkBetween(db, me, user.Id, ct);
        var status = user.Id == me ? "self" : StatusFor(link, me);
        var isFriend = link is { Status: FriendshipStatus.Accepted };
        var canView = user.Id == me || user.Profile.Visibility == ProfileVisibility.Public || isFriend;

        var lists = canView
            ? await db.ArmyLists
                .Where(l => l.UserId == user.Id)
                .Include(l => l.Entries)
                .OrderByDescending(l => l.UpdatedAt)
                .ToListAsync(ct)
            : [];

        var listDtos = lists.Select(l => new PublicListDto(
            l.Id, l.Name, l.GameSystemKey, l.FactionName, l.PointsLimit,
            l.Entries.Sum(Total), l.Entries.Count, l.UpdatedAt)).ToList();

        return Results.Ok(new PublicProfileDto(
            user.Id, user.Username, user.Profile.Visibility.ToString(),
            canView ? user.Profile.Bio : null, status, canView, listDtos));
    }

    private static async Task<IResult> GetFriends(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var rows = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterId == me || f.AddresseeId == me))
            .Select(f => new
            {
                f.RequesterId,
                f.AddresseeId,
                RequesterName = f.Requester.Username,
                RequesterVis = f.Requester.Profile.Visibility,
                AddresseeName = f.Addressee.Username,
                AddresseeVis = f.Addressee.Profile.Visibility,
            })
            .ToListAsync(ct);

        var friends = rows
            .Select(r => r.RequesterId == me
                ? new FriendDto(r.AddresseeId, r.AddresseeName, r.AddresseeVis.ToString())
                : new FriendDto(r.RequesterId, r.RequesterName, r.RequesterVis.ToString()))
            .OrderBy(f => f.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Results.Ok(friends);
    }

    private static async Task<IResult> GetRequests(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var pending = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Pending && (f.RequesterId == me || f.AddresseeId == me))
            .Select(f => new
            {
                f.Id,
                f.RequesterId,
                f.AddresseeId,
                RequesterName = f.Requester.Username,
                AddresseeName = f.Addressee.Username,
            })
            .ToListAsync(ct);

        var incoming = pending
            .Where(f => f.AddresseeId == me)
            .Select(f => new FriendRequestDto(f.Id, f.RequesterId, f.RequesterName, "incoming"))
            .ToList();
        var outgoing = pending
            .Where(f => f.RequesterId == me)
            .Select(f => new FriendRequestDto(f.Id, f.AddresseeId, f.AddresseeName, "outgoing"))
            .ToList();
        return Results.Ok(new FriendRequestsDto(incoming, outgoing));
    }

    private static async Task<IResult> SendRequest(
        SendFriendRequestBody body, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var username = body.Username?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            return Problem("username", "A username is required.");
        }

        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (target is null)
        {
            return Results.NotFound(new { error = "No user with that username." });
        }
        if (target.Id == me)
        {
            return Problem("username", "You cannot add yourself.");
        }

        var existing = await LinkBetween(db, me, target.Id, ct);
        if (existing is { Status: FriendshipStatus.Accepted })
        {
            return Results.Conflict(new { error = "You are already friends." });
        }
        if (existing is { Status: FriendshipStatus.Pending })
        {
            // They already asked you: accept instead of creating a duplicate.
            if (existing.AddresseeId == me)
            {
                existing.Status = FriendshipStatus.Accepted;
                existing.RespondedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { status = "friends" });
            }
            return Results.Conflict(new { error = "Request already sent." });
        }

        db.Friendships.Add(new Friendship { RequesterId = me, AddresseeId = target.Id });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { status = "pending_outgoing" });
    }

    private static async Task<IResult> AcceptRequest(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var request = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == me && f.Status == FriendshipStatus.Pending, ct);
        if (request is null)
        {
            return Results.NotFound();
        }
        request.Status = FriendshipStatus.Accepted;
        request.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelRequest(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var request = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == id && f.Status == FriendshipStatus.Pending && (f.RequesterId == me || f.AddresseeId == me), ct);
        if (request is null)
        {
            return Results.NotFound();
        }
        db.Friendships.Remove(request);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveFriend(Guid userId, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var link = await LinkBetween(db, me, userId, ct);
        if (link is null)
        {
            return Results.NotFound();
        }
        db.Friendships.Remove(link);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<Friendship?> LinkBetween(BattleLibraryDbContext db, Guid a, Guid b, CancellationToken ct) =>
        db.Friendships.FirstOrDefaultAsync(
            f => (f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a), ct);

    // Friendships linking me to any of the given user ids, keyed by the other user.
    private static async Task<Dictionary<Guid, Friendship>> LinksWith(
        BattleLibraryDbContext db, Guid me, IReadOnlyList<Guid> others, CancellationToken ct)
    {
        if (others.Count == 0)
        {
            return [];
        }
        var links = await db.Friendships
            .Where(f => (f.RequesterId == me && others.Contains(f.AddresseeId))
                     || (f.AddresseeId == me && others.Contains(f.RequesterId)))
            .ToListAsync(ct);
        return links.ToDictionary(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId);
    }

    private static string StatusFor(Friendship? link, Guid me)
    {
        if (link is null)
        {
            return "none";
        }
        if (link.Status == FriendshipStatus.Accepted)
        {
            return "friends";
        }
        return link.RequesterId == me ? "pending_outgoing" : "pending_incoming";
    }

    private static int Total(ListEntry e) =>
        (e.Points + e.SelectedOptions.Sum(o => o.Points)) * e.Quantity;

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
