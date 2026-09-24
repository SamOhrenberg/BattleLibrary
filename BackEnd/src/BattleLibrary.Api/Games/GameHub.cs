using System.Security.Claims;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Games;

// Real-time channel for live game sessions. Clients join a per-session group to
// receive "sessionUpdated" pings; each connection is also placed in a per-user
// group so invites and index changes reach the player wherever they are.
[Authorize]
public sealed class GameHub : Hub
{
    private readonly BattleLibraryDbContext _db;

    public GameHub(BattleLibraryDbContext db) => _db = db;

    public static string SessionGroup(Guid sessionId) => $"session:{sessionId}";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (CurrentUserId() is { } userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }
        await base.OnConnectedAsync();
    }

    // Join a session's group after verifying the caller is one of its players.
    public async Task JoinSession(Guid sessionId)
    {
        if (CurrentUserId() is not { } userId)
        {
            throw new HubException("Unauthorized.");
        }
        var isParticipant = await _db.GameParticipants
            .AnyAsync(p => p.GameSessionId == sessionId && p.UserId == userId);
        if (!isParticipant)
        {
            throw new HubException("Not a participant of this game.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(sessionId));
    }

    public Task LeaveSession(Guid sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroup(sessionId));

    private Guid? CurrentUserId() =>
        Guid.TryParse(Context.User?.FindFirstValue("sub"), out var id) ? id : null;
}
