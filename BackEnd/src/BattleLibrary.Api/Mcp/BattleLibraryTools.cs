using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using BattleLibrary.Api.Catalogue;
using BattleLibrary.Api.Games;
using BattleLibrary.Api.Lists;
using BattleLibrary.Api.Social;
using BattleLibrary.Domain.Games;
using BattleLibrary.Infrastructure.BsData.Caching;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BattleLibrary.Api.Mcp;

// Read-only MCP tools. Each wraps an existing endpoint handler, so ownership and
// profile-visibility rules match the app exactly.
[McpServerToolType]
public sealed class BattleLibraryTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "get_my_account", Title = "My account", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The signed-in player's username, profile visibility, bio, and how many lists, logged games, and friends they have.")]
    public static async Task<string> GetMyAccount(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct)
    {
        var userId = UserId(context);
        var account = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Username,
                Visibility = u.Profile.Visibility.ToString(),
                u.Profile.Bio,
                MemberSince = u.CreatedAt,
                ArmyLists = db.ArmyLists.Count(l => l.UserId == userId),
                GamesLogged = db.Games.Count(g => g.UserId == userId),
                Friends = db.Friendships.Count(f =>
                    f.Status == Domain.Social.FriendshipStatus.Accepted
                    && (f.RequesterId == userId || f.AddresseeId == userId)),
            })
            .FirstOrDefaultAsync(ct);
        return account is null ? throw new McpException("Account not found.") : Serialize(account);
    }

    [McpServerTool(Name = "list_army_lists", Title = "List my army lists", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Summaries of the player's army lists, most recently edited first: id, name, game system, faction, points total vs limit, unit count. Use get_army_list for the full contents.")]
    public static async Task<string> ListArmyLists(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct) =>
        Unwrap(await ListEndpoints.GetLists(Principal(context), db, ct));

    [McpServerTool(Name = "get_army_list", Title = "Get an army list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Full contents of one of the player's army lists: detachments, forces, and every unit with its points, model count, wargear selections, enhancement, warlord flag, and Leader attachment. Each unit's datasheetId works with get_datasheet.")]
    public static async Task<string> GetArmyList(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db,
        [Description("The list id, from list_army_lists.")] Guid listId,
        CancellationToken ct) =>
        Unwrap(await ListEndpoints.GetList(listId, Principal(context), db, ct));

    [McpServerTool(Name = "list_games", Title = "List my logged games", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The player's logged game history, newest first: date, game system, result (Win/Loss/Draw), opponent, factions, scores, linked list, and notes.")]
    public static async Task<string> ListGames(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct) =>
        Unwrap(await GameEndpoints.GetGames(Principal(context), db, ct));

    [McpServerTool(Name = "get_game", Title = "Get a logged game", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("One logged game by id.")]
    public static async Task<string> GetGame(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db,
        [Description("The game id, from list_games.")] Guid gameId,
        CancellationToken ct) =>
        Unwrap(await GameEndpoints.GetGame(gameId, Principal(context), db, ct));

    [McpServerTool(Name = "get_game_stats", Title = "My win/loss stats", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Win/loss/draw record across logged games: overall, and broken down by opponent, by the faction the player used, by opposing faction, and by game system.")]
    public static async Task<string> GetGameStats(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct)
    {
        var userId = UserId(context);
        var games = await db.Games
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Result, g.OpponentName, g.PlayerFactionName, g.OpponentFactionName, g.GameSystemKey })
            .ToListAsync(ct);

        static List<ResultRecord> Breakdown(IEnumerable<(string? Key, GameResult Result)> rows) =>
            rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Key) ? "(unknown)" : r.Key!)
                .Select(g => Record(g.Key, g.Select(r => r.Result).ToList()))
                .OrderByDescending(r => r.Games)
                .ToList();

        return Serialize(new
        {
            Overall = Record("all", games.Select(g => g.Result).ToList()),
            ByOpponent = Breakdown(games.Select(g => ((string?)g.OpponentName, g.Result))),
            ByMyFaction = Breakdown(games.Select(g => (g.PlayerFactionName, g.Result))),
            ByOpponentFaction = Breakdown(games.Select(g => (g.OpponentFactionName, g.Result))),
            ByGameSystem = Breakdown(games.Select(g => ((string?)g.GameSystemKey, g.Result))),
        });
    }

    [McpServerTool(Name = "list_live_games", Title = "List my live games", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Live multiplayer game sessions the player is invited to or playing in (setup or in progress).")]
    public static async Task<string> ListLiveGames(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct) =>
        Unwrap(await GameSessionEndpoints.MySessions(Principal(context), db, ct));

    [McpServerTool(Name = "get_live_game", Title = "Get a live game", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Full state of a live game session the player is part of: current round, each player's score by round (primary and secondaries), command points, logged stratagems, and every unit's remaining models.")]
    public static async Task<string> GetLiveGame(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CatalogueStore catalogue,
        [Description("The session id, from list_live_games or a logged game's gameSessionId.")] Guid sessionId,
        CancellationToken ct) =>
        Unwrap(await GameSessionEndpoints.Get(sessionId, Principal(context), db, catalogue, ct));

    [McpServerTool(Name = "list_friends", Title = "List my friends", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The player's friends, plus pending incoming and outgoing friend requests.")]
    public static async Task<string> ListFriends(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db, CancellationToken ct)
    {
        var principal = Principal(context);
        var friends = Value(await SocialEndpoints.GetFriends(principal, db, ct));
        var requests = Value(await SocialEndpoints.GetRequests(principal, db, ct));
        return Serialize(new { friends, requests });
    }

    [McpServerTool(Name = "get_player_profile", Title = "Get a player's profile", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("A player's profile by username. Lists are included only when the profile is public, the player is a friend, or it is the signed-in player.")]
    public static async Task<string> GetPlayerProfile(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db,
        [Description("Exact username (case-insensitive).")] string username,
        CancellationToken ct) =>
        Unwrap(await SocialEndpoints.GetPublicProfile(username, Principal(context), db, ct));

    [McpServerTool(Name = "search_players", Title = "Search players", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Find other players whose username contains the text (up to 20), with the friendship status for each.")]
    public static async Task<string> SearchPlayers(
        RequestContext<CallToolRequestParams> context, BattleLibraryDbContext db,
        [Description("Part of a username.")] string query,
        CancellationToken ct) =>
        Unwrap(await SocialEndpoints.SearchUsers(query, Principal(context), db, ct));

    [McpServerTool(Name = "list_game_systems", Title = "List game systems", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Game systems loaded from community BSData (for example wh40k-11e and kill-team-2024), with faction and datasheet counts.")]
    public static string ListGameSystems(CatalogueStore catalogue) =>
        Unwrap(CatalogueEndpoints.GetSystems(Ready(catalogue)));

    [McpServerTool(Name = "list_factions", Title = "List factions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Factions (or kill teams) in a game system, with their ids.")]
    public static string ListFactions(
        CatalogueStore catalogue,
        [Description("Game system key, from list_game_systems (for example wh40k-11e).")] string systemKey) =>
        Unwrap(CatalogueEndpoints.GetFactions(systemKey, Ready(catalogue)));

    [McpServerTool(Name = "get_faction", Title = "Get a faction", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("A faction's detachments (with enhancements and rules) and every datasheet it can field, with points and keywords. Large for big factions.")]
    public static string GetFaction(
        CatalogueStore catalogue,
        [Description("Faction id, from list_factions or an army list's factionId.")] string factionId) =>
        Unwrap(CatalogueEndpoints.GetDatasheets(factionId, Ready(catalogue)));

    [McpServerTool(Name = "get_datasheet", Title = "Get a datasheet", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("One datasheet: stat line, weapon profiles, abilities, keywords, points by unit size, and wargear options.")]
    public static string GetDatasheet(
        CatalogueStore catalogue,
        [Description("Datasheet id, from get_faction, search_rules, or an army list entry's datasheetId.")] string datasheetId) =>
        Unwrap(CatalogueEndpoints.GetDatasheet(datasheetId, Ready(catalogue)));

    [McpServerTool(Name = "search_rules", Title = "Search rules", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search a game system's datasheets, weapons, abilities, detachment rules, and enhancements by name or text.")]
    public static string SearchRules(
        CatalogueStore catalogue,
        [Description("Game system key (for example wh40k-11e).")] string systemKey,
        [Description("At least 2 characters.")] string query) =>
        Unwrap(CatalogueEndpoints.Search(systemKey, query, Ready(catalogue)));

    private sealed record ResultRecord(string Key, int Games, int Wins, int Losses, int Draws, double WinRate);

    private static ResultRecord Record(string key, IReadOnlyList<GameResult> results)
    {
        var wins = results.Count(r => r == GameResult.Win);
        var total = results.Count;
        return new ResultRecord(
            key, total, wins,
            results.Count(r => r == GameResult.Loss),
            results.Count(r => r == GameResult.Draw),
            total == 0 ? 0 : Math.Round(wins * 100.0 / total, 1));
    }

    private static ClaimsPrincipal Principal(RequestContext<CallToolRequestParams> context) =>
        context.User ?? throw new McpException("Not signed in.");

    private static Guid UserId(RequestContext<CallToolRequestParams> context) =>
        Guid.TryParse(Principal(context).FindFirstValue("sub"), out var id) ? id : throw new McpException("Not signed in.");

    private static CatalogueStore Ready(CatalogueStore catalogue) =>
        catalogue.Ready ? catalogue : throw new McpException("The catalogue is still loading. Try again in a few seconds.");

    private static string Unwrap(IResult result) => Serialize(Value(result));

    // Turns an endpoint result into its payload, or a tool error for 4xx/5xx.
    private static object? Value(IResult result)
    {
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        var value = (result as IValueHttpResult)?.Value;
        if (status < 400)
        {
            return value;
        }
        throw new McpException(status switch
        {
            StatusCodes.Status404NotFound => "Not found, or not visible to you.",
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "Not allowed.",
            _ when value is HttpValidationProblemDetails v => string.Join(" ", v.Errors.SelectMany(e => e.Value)),
            _ when value is ProblemDetails p => p.Detail ?? p.Title ?? "Request failed.",
            _ => $"Request failed ({status}).",
        });
    }

    private static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);
}
