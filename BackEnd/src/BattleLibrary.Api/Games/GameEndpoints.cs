using System.Security.Claims;
using BattleLibrary.Domain.Games;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Games;

public static class GameEndpoints
{
    public static RouteGroupBuilder MapGameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/games").RequireAuthorization();

        group.MapGet("", GetGames);
        group.MapGet("/stats", GetStats);
        group.MapPost("", RecordGame);
        group.MapGet("/{id:guid}", GetGame);
        group.MapPut("/{id:guid}", UpdateGame);
        group.MapDelete("/{id:guid}", DeleteGame);

        return group;
    }

    private static async Task<IResult> GetGames(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var games = await db.Games
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.PlayedOn)
            .ThenByDescending(g => g.CreatedAt)
            .Select(ToDto)
            .ToListAsync(ct);
        return Results.Ok(games);
    }

    private static async Task<IResult> GetStats(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var counts = await db.Games
            .Where(g => g.UserId == userId)
            .GroupBy(g => g.Result)
            .Select(grp => new { grp.Key, Count = grp.Count() })
            .ToListAsync(ct);

        var wins = counts.FirstOrDefault(c => c.Key == GameResult.Win)?.Count ?? 0;
        var losses = counts.FirstOrDefault(c => c.Key == GameResult.Loss)?.Count ?? 0;
        var draws = counts.FirstOrDefault(c => c.Key == GameResult.Draw)?.Count ?? 0;
        var total = wins + losses + draws;
        var winRate = total == 0 ? 0 : Math.Round(wins * 100.0 / total, 1);
        return Results.Ok(new GameStatsDto(total, wins, losses, draws, winRate));
    }

    private static async Task<IResult> GetGame(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var game = await db.Games
            .Where(g => g.Id == id && g.UserId == userId)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);
        return game is null ? Results.NotFound() : Results.Ok(game);
    }

    private static async Task<IResult> RecordGame(
        RecordGameRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var game = new Game { UserId = userId };
        if (await Apply(game, request, userId, db, ct) is { } error)
        {
            return error;
        }
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/games/{game.Id}", await Reload(db, game.Id, userId, ct));
    }

    private static async Task<IResult> UpdateGame(
        Guid id, RecordGameRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (game is null)
        {
            return Results.NotFound();
        }
        if (await Apply(game, request, userId, db, ct) is { } error)
        {
            return error;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Reload(db, game.Id, userId, ct));
    }

    private static async Task<IResult> DeleteGame(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (game is null)
        {
            return Results.NotFound();
        }
        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Validates the request and writes it onto the game; returns an error result
    // on failure, or null on success.
    private static async Task<IResult?> Apply(
        Game game, RecordGameRequest request, Guid userId, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (!Enum.TryParse<GameResult>(request.Result, ignoreCase: true, out var result))
        {
            return Problem("result", "Result must be 'Win', 'Loss', or 'Draw'.");
        }
        if (request.PlayerScore is < 0 || request.OpponentScore is < 0)
        {
            return Problem("score", "Scores cannot be negative.");
        }
        var notes = request.Notes?.Trim();
        if (notes is { Length: > 1000 })
        {
            return Problem("notes", "Notes must be 1000 characters or fewer.");
        }

        // Opponent: a registered user takes precedence; otherwise a stranger name.
        Guid? opponentUserId = null;
        string opponentName;
        if (request.OpponentUserId is { } opponentId)
        {
            if (opponentId == userId)
            {
                return Problem("opponent", "You cannot record a game against yourself.");
            }
            var opponent = await db.Users.FirstOrDefaultAsync(u => u.Id == opponentId, ct);
            if (opponent is null)
            {
                return Results.NotFound(new { error = "Opponent user not found." });
            }
            opponentUserId = opponent.Id;
            opponentName = opponent.Username;
        }
        else
        {
            opponentName = request.OpponentName?.Trim() ?? string.Empty;
            if (opponentName.Length is < 1 or > 80)
            {
                return Problem("opponent", "An opponent name (1-80 characters) is required.");
            }
        }

        // Optional link to one of the owner's own lists; defaults faction/system.
        string? playerFaction = Clean(request.PlayerFactionName, 200);
        string gameSystemKey = Clean(request.GameSystemKey, 100) ?? string.Empty;
        Guid? playerListId = null;
        if (request.PlayerListId is { } listId)
        {
            var list = await db.ArmyLists
                .Where(l => l.Id == listId && l.UserId == userId)
                .Select(l => new { l.Id, l.FactionName, l.GameSystemKey })
                .FirstOrDefaultAsync(ct);
            if (list is null)
            {
                return Results.NotFound(new { error = "That army list was not found." });
            }
            playerListId = list.Id;
            playerFaction ??= list.FactionName;
            if (gameSystemKey.Length == 0)
            {
                gameSystemKey = list.GameSystemKey;
            }
        }

        game.PlayedOn = request.PlayedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        game.GameSystemKey = gameSystemKey;
        game.Result = result;
        game.OpponentUserId = opponentUserId;
        game.OpponentName = opponentName;
        game.PlayerFactionName = playerFaction;
        game.OpponentFactionName = Clean(request.OpponentFactionName, 200);
        game.PlayerListId = playerListId;
        game.PlayerScore = request.PlayerScore;
        game.OpponentScore = request.OpponentScore;
        game.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        return null;
    }

    private static Task<GameDto?> Reload(BattleLibraryDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.Games.Where(g => g.Id == id && g.UserId == userId).Select(ToDto).FirstOrDefaultAsync(ct);

    // Projection to GameDto; the player list name is read via the nav, which is
    // null once the referenced list is deleted.
    private static readonly System.Linq.Expressions.Expression<Func<Game, GameDto>> ToDto = g => new GameDto(
        g.Id,
        g.PlayedOn,
        g.GameSystemKey,
        g.Result.ToString(),
        g.OpponentUserId,
        g.OpponentName,
        g.PlayerFactionName,
        g.OpponentFactionName,
        g.PlayerListId,
        g.PlayerList != null ? g.PlayerList.Name : null,
        g.PlayerScore,
        g.OpponentScore,
        g.Notes,
        g.GameSessionId);

    private static string? Clean(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
