using System.Security.Claims;
using BattleLibrary.Domain.Games;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.BsData.Caching;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Games;

public static class GameSessionEndpoints
{
    private const int MaxParticipants = 4;
    private const int MaxUnits = 100;

    // Battle sizes and their points caps (40k 11e). A named size sets the cap
    // authoritatively; a null size means a custom or point-less game.
    private static readonly IReadOnlyDictionary<string, int> BattleSizeCaps = new Dictionary<string, int>
    {
        ["Incursion"] = 1000,
        ["Strike Force"] = 2000,
        ["Onslaught"] = 3000,
    };

    public static RouteGroupBuilder MapGameSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/game-sessions").RequireAuthorization();

        group.MapGet("", MySessions);
        group.MapPost("", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapPost("/{id:guid}/invite", Invite);
        group.MapPost("/{id:guid}/respond", Respond);
        group.MapPost("/{id:guid}/list", SetList);
        group.MapPost("/{id:guid}/leave", Leave);
        group.MapPost("/{id:guid}/start", Start);
        group.MapPost("/{id:guid}/advance", Advance);
        group.MapPut("/{id:guid}/rounds/{round:int}", SetRound);
        group.MapPut("/{id:guid}/units/{unitId:guid}", UpdateUnit);
        group.MapPost("/{id:guid}/actions", LogAction);
        group.MapDelete("/{id:guid}/actions/{actionId:guid}", DeleteAction);
        group.MapPost("/{id:guid}/finish", Finish);

        return group;
    }

    private static async Task<IResult> MySessions(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var sessions = await db.GameSessions
            .Include(s => s.Participants)
            .Where(s => (s.Status == GameSessionStatus.Setup || s.Status == GameSessionStatus.Active)
                && s.Participants.Any(p => p.UserId == me
                    && (p.Status == ParticipantStatus.Invited || p.Status == ParticipantStatus.Accepted)))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var summaries = sessions.Select(s =>
        {
            var mine = s.Participants.First(p => p.UserId == me);
            var active = s.Participants.Count(p => p.Status is ParticipantStatus.Invited or ParticipantStatus.Accepted);
            return new SessionSummaryDto(s.Id, s.Name, s.GameSystemKey, s.PointsLimit, s.Status.ToString(),
                s.CurrentRound, s.CreatedByUserId == me, mine.Status.ToString(), active, s.CreatedAt);
        });
        return Results.Ok(summaries);
    }

    private static async Task<IResult> Create(
        CreateSessionRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var name = request.Name?.Trim();
        if (name is { Length: > 120 })
        {
            return Problem("name", "Name must be 120 characters or fewer.");
        }
        if (request.PointsLimit is < 0)
        {
            return Problem("pointsLimit", "Points limit cannot be negative.");
        }

        // A named battle size sets the points cap; a custom game keeps the raw
        // limit. An unknown size name is rejected.
        var battleSize = string.IsNullOrWhiteSpace(request.BattleSize) ? null : request.BattleSize.Trim();
        if (battleSize is not null && !BattleSizeCaps.ContainsKey(battleSize))
        {
            return Problem("battleSize", "Unknown battle size.");
        }
        var pointsLimit = battleSize is not null ? BattleSizeCaps[battleSize] : request.PointsLimit;

        var session = new GameSession
        {
            CreatedByUserId = me,
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            GameSystemKey = request.GameSystemKey?.Trim() ?? string.Empty,
            BattleSize = battleSize,
            PointsLimit = pointsLimit,
            StartingCp = Math.Max(0, request.StartingCp ?? 0),
        };
        session.Participants.Add(new GameParticipant
        {
            GameSessionId = session.Id,
            UserId = me,
            Status = ParticipantStatus.Accepted,
            Seat = 0,
        });
        db.GameSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var full = await LoadTracked(db, session.Id, ct);
        return Results.Created($"/api/game-sessions/{session.Id}", ToDto(full!, me));
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        if (session is null || session.Participants.All(p => p.UserId != me))
        {
            return Results.NotFound();
        }
        // During setup, surface each accepted player's list legality so the host
        // (and players) can see what's blocking the start before pressing it.
        Dictionary<Guid, string?>? legality = null;
        if (session.Status == GameSessionStatus.Setup)
        {
            var accepted = session.Participants.Where(p => p.Status == ParticipantStatus.Accepted).ToList();
            legality = await ListIssuesByParticipant(db, session, accepted, ct);
        }
        return Results.Ok(ToDto(session, me, legality));
    }

    private static async Task<IResult> Invite(
        Guid id, InviteRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        if (session is null || session.CreatedByUserId != me)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Setup)
        {
            return Problem("status", "Players can only be invited while the game is being set up.");
        }
        var active = session.Participants.Count(p => p.Status is ParticipantStatus.Invited or ParticipantStatus.Accepted);
        if (active >= MaxParticipants)
        {
            return Problem("participants", $"A game holds at most {MaxParticipants} players.");
        }

        var username = request.Username?.Trim() ?? string.Empty;
        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (target is null)
        {
            return Results.NotFound(new { error = "No user with that username." });
        }

        var existing = session.Participants.FirstOrDefault(p => p.UserId == target.Id);
        if (existing is { Status: ParticipantStatus.Invited or ParticipantStatus.Accepted })
        {
            return Results.Conflict(new { error = "That player is already in the game." });
        }
        if (existing is not null)
        {
            // Re-invite someone who had declined or left.
            existing.Status = ParticipantStatus.Invited;
        }
        else
        {
            var seat = session.Participants.Count == 0 ? 0 : session.Participants.Max(p => p.Seat) + 1;
            session.Participants.Add(new GameParticipant
            {
                GameSessionId = session.Id,
                UserId = target.Id,
                Status = ParticipantStatus.Invited,
                Seat = seat,
            });
        }
        await db.SaveChangesAsync(ct);
        var full = await LoadTracked(db, id, ct);
        await Broadcast(hub, full!, ct);
        return Results.Ok(ToDto(full!, me));
    }

    private static async Task<IResult> Respond(
        Guid id, RespondRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        if (session is null || mine is null)
        {
            return Results.NotFound();
        }
        if (mine.Status != ParticipantStatus.Invited)
        {
            return Problem("status", "You have no pending invite for this game.");
        }
        mine.Status = request.Accept ? ParticipantStatus.Accepted : ParticipantStatus.Declined;
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> SetList(
        Guid id, SetListRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        if (session is null || mine is null)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Setup)
        {
            return Problem("status", "Lists are locked once the game starts.");
        }
        if (mine.Status != ParticipantStatus.Accepted)
        {
            return Problem("status", "Accept the invite before choosing a list.");
        }

        if (request.ListId is not { } listId)
        {
            mine.ArmyListId = null;
            mine.FactionName = null;
            mine.Units = [];
        }
        else
        {
            var list = await db.ArmyLists
                .Include(l => l.Entries)
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == me, ct);
            if (list is null)
            {
                return Results.NotFound(new { error = "That army list was not found." });
            }
            if (ValidateList(list, session) is { } problem)
            {
                return Problem("list", $"That list can't be used: {problem}");
            }
            // One unit per entry; the entry's model count is what you may bring
            // (default full, capped so you can't field more than the list).
            var units = new List<GameUnit>();
            foreach (var e in list.Entries.OrderBy(e => e.DatasheetName).Take(MaxUnits))
            {
                var models = Math.Max(1, e.ModelCount);
                units.Add(new GameUnit
                {
                    DatasheetId = e.DatasheetId,
                    Name = e.DatasheetName,
                    MaxModels = models,
                    StartingModels = models,
                    CurrentModels = models,
                    Keywords = [.. e.Keywords],
                    IsWarlord = e.IsWarlord,
                    EnhancementName = e.EnhancementName,
                    Points = (e.Points + e.SelectedOptions.Sum(o => o.Points)) * e.Quantity + e.EnhancementPoints,
                    Wargear = [.. e.SelectedOptions.Select(o => o.Name)],
                });
            }
            mine.ArmyListId = list.Id;
            mine.FactionName = list.FactionName;
            mine.Units = units;
            if (session.GameSystemKey.Length == 0)
            {
                session.GameSystemKey = list.GameSystemKey;
            }
        }
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> Leave(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        if (session is null || mine is null)
        {
            return Results.NotFound();
        }

        if (session.CreatedByUserId == me)
        {
            session.Status = GameSessionStatus.Abandoned;
        }
        else
        {
            mine.Status = ParticipantStatus.Left;
        }
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Start(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        if (session is null || session.CreatedByUserId != me)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Setup)
        {
            return Problem("status", "This game has already started.");
        }
        var players = session.Participants.Where(p => p.Status == ParticipantStatus.Accepted).ToList();
        if (players.Count < 2)
        {
            return Problem("participants", "At least two players must accept before starting.");
        }

        // Every player needs a valid, legal army list before the game can begin.
        var issues = await ListIssuesByParticipant(db, session, players, ct);
        foreach (var p in players)
        {
            if (p.ArmyListId is null)
            {
                return Problem("lists", $"{p.User.Username} has not chosen an army list yet.");
            }
            if (issues.GetValueOrDefault(p.Id) is { } reason)
            {
                return Problem("lists", $"{p.User.Username}'s list can't be used: {reason}");
            }
        }

        session.Status = GameSessionStatus.Active;
        session.CurrentRound = 1;
        session.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> Advance(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        if (session is null || session.CreatedByUserId != me)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Active)
        {
            return Problem("status", "The game is not in progress.");
        }
        session.CurrentRound += 1;
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> SetRound(
        Guid id, int round, RoundScoreRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        if (session is null || mine is null)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Active)
        {
            return Problem("status", "Scores can only be set while the game is in progress.");
        }
        if (round < 1 || round > session.CurrentRound)
        {
            return Problem("round", "That round has not been reached yet.");
        }
        if (request.PrimaryScore < 0 || request.CpGained < 0)
        {
            return Problem("score", "Values cannot be negative.");
        }

        var secondaries = (request.Secondaries ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Take(8)
            .Select(s => new SecondaryEntry { Name = s.Name.Trim()[..Math.Min(s.Name.Trim().Length, 80)], Score = Math.Max(0, s.Score) })
            .ToList();

        var record = mine.Rounds.FirstOrDefault(r => r.RoundNumber == round);
        if (record is null)
        {
            record = new GameRoundRecord { RoundNumber = round };
            mine.Rounds.Add(record);
        }
        record.PrimaryScore = request.PrimaryScore;
        record.CpGained = request.CpGained;
        record.Secondaries = secondaries;

        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> UpdateUnit(
        Guid id, Guid unitId, UpdateUnitRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        var unit = mine?.Units.FirstOrDefault(u => u.Id == unitId);
        if (session is null || mine is null || unit is null)
        {
            return Results.NotFound();
        }

        if (session.Status == GameSessionStatus.Setup)
        {
            // During setup the player may bring fewer models than the list, never
            // more; the cap is the list count (fallback for legacy units).
            if (request.StartingModels is { } starting)
            {
                var cap = unit.MaxModels > 0 ? unit.MaxModels : 999;
                unit.StartingModels = Math.Clamp(starting, 1, cap);
                unit.CurrentModels = unit.StartingModels;
                unit.Destroyed = false;
            }
        }
        else if (session.Status == GameSessionStatus.Active)
        {
            if (request.CurrentModels is { } current)
            {
                unit.CurrentModels = Math.Clamp(current, 0, unit.StartingModels);
            }
            if (request.Destroyed is { } destroyed)
            {
                unit.Destroyed = destroyed;
                if (destroyed)
                {
                    unit.CurrentModels = 0;
                }
            }
            unit.Destroyed = unit.CurrentModels == 0;
        }
        else
        {
            return Problem("status", "The game is no longer editable.");
        }

        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> LogAction(
        Guid id, LogActionRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        if (session is null || mine is null)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Active)
        {
            return Problem("status", "The game is not in progress.");
        }
        if (!Enum.TryParse<GameActionKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Problem("kind", "Kind must be 'Ability' or 'Stratagem'.");
        }
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
        {
            return Problem("name", "A name (1-100 characters) is required.");
        }
        if (request.CpCost is < 0)
        {
            return Problem("cpCost", "CP cost cannot be negative.");
        }

        mine.Actions.Add(new GameAction
        {
            RoundNumber = request.RoundNumber is { } r && r >= 1 ? r : Math.Max(1, session.CurrentRound),
            Kind = kind,
            Name = name,
            CpCost = request.CpCost,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()[..Math.Min(request.Note.Trim().Length, 200)],
        });
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> DeleteAction(
        Guid id, Guid actionId, ClaimsPrincipal principal, BattleLibraryDbContext db,
        IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        var mine = session?.Participants.FirstOrDefault(p => p.UserId == me);
        var action = mine?.Actions.FirstOrDefault(a => a.Id == actionId);
        if (session is null || mine is null || action is null)
        {
            return Results.NotFound();
        }
        mine.Actions.Remove(action);
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    private static async Task<IResult> Finish(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, IHubContext<GameHub> hub, CancellationToken ct)
    {
        if (Me(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var session = await LoadTracked(db, id, ct);
        if (session is null || session.CreatedByUserId != me)
        {
            return Results.NotFound();
        }
        if (session.Status != GameSessionStatus.Active)
        {
            return Problem("status", "Only a game in progress can be finished.");
        }

        var players = session.Participants.Where(p => p.Status == ParticipantStatus.Accepted).ToList();
        var totals = players.ToDictionary(p => p.Id, Total);
        var best = totals.Count == 0 ? 0 : totals.Values.Max();
        var winners = totals.Count(kv => kv.Value == best);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Keep the history link only to lists that still exist (FK is SetNull).
        var listIds = players.Where(p => p.ArmyListId is not null).Select(p => p.ArmyListId!.Value).ToList();
        var liveListIds = listIds.Count == 0
            ? []
            : await db.ArmyLists.Where(l => listIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);

        foreach (var p in players)
        {
            var mineTotal = totals[p.Id];
            var others = players.Where(o => o.Id != p.Id).ToList();
            var result = mineTotal == best
                ? (winners == 1 ? GameResult.Win : GameResult.Draw)
                : GameResult.Loss;

            Guid? opponentUserId = null;
            string opponentName;
            string? opponentFaction = null;
            int? opponentScore;
            if (others.Count == 1)
            {
                opponentUserId = others[0].UserId;
                opponentName = others[0].User.Username;
                opponentFaction = others[0].FactionName;
                opponentScore = totals[others[0].Id];
            }
            else
            {
                opponentName = $"{players.Count}-player game";
                opponentScore = others.Count == 0 ? null : others.Max(o => totals[o.Id]);
            }

            db.Games.Add(new Game
            {
                UserId = p.UserId,
                PlayedOn = today,
                GameSystemKey = session.GameSystemKey,
                Result = result,
                OpponentUserId = opponentUserId,
                OpponentName = opponentName,
                PlayerFactionName = p.FactionName,
                OpponentFactionName = opponentFaction,
                PlayerListId = p.ArmyListId is { } lid && liveListIds.Contains(lid) ? lid : null,
                PlayerScore = mineTotal,
                OpponentScore = opponentScore,
                Notes = session.Name is { Length: > 0 } ? $"Live game: {session.Name}" : "Live game",
                GameSessionId = session.Id,
            });
        }

        session.Status = GameSessionStatus.Finished;
        session.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await Broadcast(hub, session, ct);
        return Results.Ok(ToDto(session, me));
    }

    // ---- helpers ----

    private static Task<GameSession?> LoadTracked(BattleLibraryDbContext db, Guid id, CancellationToken ct) =>
        db.GameSessions
            .Include(s => s.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    private static async Task Broadcast(IHubContext<GameHub> hub, GameSession s, CancellationToken ct)
    {
        await hub.Clients.Group(GameHub.SessionGroup(s.Id)).SendAsync("sessionUpdated", s.Id, ct);
        foreach (var uid in s.Participants.Select(p => p.UserId).Distinct())
        {
            await hub.Clients.Group(GameHub.UserGroup(uid)).SendAsync("sessionsChanged", s.Id, ct);
        }
    }

    private static int Total(GameParticipant p) =>
        p.Rounds.Sum(r => r.PrimaryScore + r.Secondaries.Sum(x => x.Score));

    private static int ListTotal(ArmyList l) =>
        l.Entries.Sum(e => (e.Points + e.SelectedOptions.Sum(o => o.Points)) * e.Quantity + e.EnhancementPoints);

    // Legality reason per participant that has chosen a list: null when the list
    // is both usable (system/points/non-empty) and legal (the builder's 11e
    // rules). Participants with no list are left out of the map.
    private static async Task<Dictionary<Guid, string?>> ListIssuesByParticipant(
        BattleLibraryDbContext db, GameSession session, IReadOnlyList<GameParticipant> players, CancellationToken ct)
    {
        var withLists = players.Where(p => p.ArmyListId is not null).ToList();
        var listIds = withLists.Select(p => p.ArmyListId!.Value).Distinct().ToList();
        var lists = listIds.Count == 0
            ? []
            : await db.ArmyLists.Include(l => l.Entries).Where(l => listIds.Contains(l.Id)).ToListAsync(ct);

        // Catalogue context for legality: which systems are point-less, and which
        // of the chosen factions offer detachments (so "needs a detachment" fires
        // only where one exists).
        var sysKeys = lists.Select(l => l.GameSystemKey).Distinct().ToList();
        var factionIds = lists.Select(l => l.FactionId).Distinct().ToList();
        var pointedSystems = sysKeys.Count == 0
            ? []
            : await db.CachedDatasheets
                .Where(d => sysKeys.Contains(d.Faction.GameSystemKey) && d.Points != null)
                .Select(d => d.Faction.GameSystemKey)
                .Distinct()
                .ToListAsync(ct);
        var detachmentFactions = sysKeys.Count == 0
            ? new HashSet<(string, string)>()
            : (await db.CachedFactions
                .Where(f => sysKeys.Contains(f.GameSystemKey) && factionIds.Contains(f.BsDataId)
                    && f.DetachmentsJson != "" && f.DetachmentsJson != "[]")
                .Select(f => new { f.GameSystemKey, f.BsDataId })
                .ToListAsync(ct))
                .Select(f => (f.GameSystemKey, f.BsDataId))
                .ToHashSet();

        var map = new Dictionary<Guid, string?>();
        foreach (var p in withLists)
        {
            var list = lists.FirstOrDefault(l => l.Id == p.ArmyListId!.Value);
            if (list is null)
            {
                map[p.Id] = "that army list was not found.";
                continue;
            }
            if (ValidateList(list, session) is { } basic)
            {
                map[p.Id] = basic;
                continue;
            }
            var pointless = !pointedSystems.Contains(list.GameSystemKey);
            var hasDetachments = detachmentFactions.Contains((list.GameSystemKey, list.FactionId));
            map[p.Id] = ListLegality.FirstIssue(list, hasDetachments, pointless);
        }
        return map;
    }

    // A list is usable in a game when it is non-empty, matches the game's system,
    // and fits the game's points limit. Returns null when valid, else the reason.
    private static string? ValidateList(ArmyList list, GameSession session)
    {
        if (session.GameSystemKey.Length > 0 && list.GameSystemKey != session.GameSystemKey)
        {
            return "it is for a different game system than this game.";
        }
        if (list.Entries.Count == 0)
        {
            return "it has no units yet.";
        }
        if (session.PointsLimit is { } limit && ListTotal(list) > limit)
        {
            return $"it is {ListTotal(list)} pts, over this game's {limit} pt limit.";
        }
        return null;
    }

    private static SessionDto ToDto(GameSession s, Guid me, IReadOnlyDictionary<Guid, string?>? legality = null)
    {
        var participants = s.Participants
            .OrderBy(p => p.Seat)
            .Select(p => ToParticipant(p, me, s.StartingCp, legality?.GetValueOrDefault(p.Id)))
            .ToList();
        return new SessionDto(
            s.Id, s.Name, s.GameSystemKey, s.PointsLimit, s.BattleSize, s.StartingCp, s.Status.ToString(), s.CurrentRound,
            s.CreatedByUserId, s.CreatedByUserId == me, MaxParticipants, s.StartedAt, s.FinishedAt, participants);
    }

    private static ParticipantDto ToParticipant(GameParticipant p, Guid me, int startingCp, string? listIssue = null)
    {
        var units = p.Units
            .Select(u => new UnitDto(
                u.Id, u.DatasheetId, u.Name, u.MaxModels, u.StartingModels, u.CurrentModels, u.Destroyed,
                u.Keywords, u.IsWarlord, u.EnhancementName, u.Points, u.Wargear))
            .ToList();
        // CP spent is the sum of each round's logged stratagem/ability costs, so
        // logging a stratagem's cost is the only place spend is entered.
        var spentByRound = p.Actions
            .GroupBy(a => a.RoundNumber)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.CpCost ?? 0));
        var rounds = p.Rounds
            .OrderBy(r => r.RoundNumber)
            .Select(r =>
            {
                var secs = r.Secondaries.Select(x => new SecondaryDto(x.Name, x.Score)).ToList();
                var secTotal = secs.Sum(x => x.Score);
                var cpSpent = spentByRound.GetValueOrDefault(r.RoundNumber);
                return new RoundDto(r.RoundNumber, r.PrimaryScore, r.CpGained, cpSpent, secs, secTotal, r.PrimaryScore + secTotal);
            })
            .ToList();
        var actions = p.Actions
            .OrderBy(a => a.RoundNumber)
            .Select(a => new ActionDto(a.Id, a.RoundNumber, a.Kind.ToString(), a.Name, a.CpCost, a.Note))
            .ToList();

        var currentCp = startingCp + p.Rounds.Sum(r => r.CpGained) - p.Actions.Sum(a => a.CpCost ?? 0);
        return new ParticipantDto(
            p.Id, p.UserId, p.User.Username, p.Status.ToString(), p.Seat, p.ArmyListId, p.FactionName,
            p.UserId == me,
            rounds.Sum(r => r.RoundTotal),
            currentCp,
            units.Count,
            units.Count(u => u.Destroyed),
            units, rounds, actions, listIssue);
    }

    private static Guid? Me(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
