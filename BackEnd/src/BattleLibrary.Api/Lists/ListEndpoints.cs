using System.Security.Claims;
using System.Text.Json;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Lists;

public static class ListEndpoints
{
    // Cached datasheet jsonb is serialized with web defaults (camelCase).
    private static readonly JsonSerializerOptions SlimJson = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapListEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/lists").RequireAuthorization();

        group.MapGet("", GetLists);
        group.MapPost("", CreateList);
        group.MapGet("/{id:guid}", GetList);
        group.MapPut("/{id:guid}", UpdateList);
        group.MapDelete("/{id:guid}", DeleteList);
        group.MapPost("/{id:guid}/entries", AddEntry);
        group.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntry);
        group.MapDelete("/{id:guid}/entries/{entryId:guid}", DeleteEntry);

        return group;
    }

    private static async Task<IResult> GetLists(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var lists = await db.ArmyLists
            .Where(l => l.UserId == userId)
            .Include(l => l.Entries)
            .OrderByDescending(l => l.UpdatedAt)
            .ToListAsync(ct);

        var summaries = lists.Select(l => new ArmyListSummaryDto(
            l.Id, l.Name, l.GameSystemKey, l.FactionId, l.FactionName, l.PointsLimit,
            l.Entries.Sum(LineTotal),
            l.Entries.Count,
            l.UpdatedAt));
        return Results.Ok(summaries);
    }

    private static async Task<IResult> CreateList(
        CreateListRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120)
        {
            return Problem("name", "Name must be 1-120 characters.");
        }
        if (string.IsNullOrWhiteSpace(request.GameSystemKey) || string.IsNullOrWhiteSpace(request.FactionId))
        {
            return Problem("faction", "A game system and faction are required.");
        }
        if (request.PointsLimit is < 0)
        {
            return Problem("pointsLimit", "Points limit cannot be negative.");
        }

        var factionName = await db.CachedFactions
            .Where(f => f.BsDataId == request.FactionId && f.GameSystemKey == request.GameSystemKey)
            .Where(f => db.CachedGameSystems.Any(s => s.Key == request.GameSystemKey && s.Enabled))
            .Select(f => f.Name)
            .FirstOrDefaultAsync(ct);
        if (factionName is null)
        {
            return Results.NotFound(new { error = "Unknown game system or faction." });
        }

        var list = new ArmyList
        {
            UserId = userId,
            Name = name,
            GameSystemKey = request.GameSystemKey,
            FactionId = request.FactionId,
            FactionName = factionName,
            PointsLimit = request.PointsLimit,
        };
        db.ArmyLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lists/{list.Id}", ToDto(list));
    }

    private static async Task<IResult> GetList(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        return list is null ? Results.NotFound() : Results.Ok(ToDto(list));
    }

    private static async Task<IResult> UpdateList(
        Guid id, UpdateListRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        if (list is null)
        {
            return Results.NotFound();
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120)
        {
            return Problem("name", "Name must be 1-120 characters.");
        }
        if (request.PointsLimit is < 0)
        {
            return Problem("pointsLimit", "Points limit cannot be negative.");
        }

        list.Name = name;
        list.PointsLimit = request.PointsLimit;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> DeleteList(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await db.ArmyLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct);
        if (list is null)
        {
            return Results.NotFound();
        }
        db.ArmyLists.Remove(list);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddEntry(
        Guid id, AddEntryRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        if (list is null)
        {
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.DatasheetId))
        {
            return Problem("datasheetId", "A datasheet is required.");
        }
        var quantity = request.Quantity ?? 1;
        if (quantity is < 1 or > 99)
        {
            return Problem("quantity", "Quantity must be 1-99.");
        }

        var sheet = await LoadSheet(db, request.DatasheetId, list.GameSystemKey, ct);
        if (sheet is null)
        {
            return Results.NotFound(new { error = "Unknown datasheet for this list's game system." });
        }

        list.Entries.Add(new ListEntry
        {
            ArmyListId = list.Id,
            DatasheetId = request.DatasheetId,
            DatasheetName = sheet.Name,
            Points = sheet.Points,
            Quantity = quantity,
            SelectedOptions = ResolveOptions(sheet.Options, request.SelectedOptionIds),
        });
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> UpdateEntry(
        Guid id, Guid entryId, UpdateEntryRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        var entry = list?.Entries.FirstOrDefault(e => e.Id == entryId);
        if (list is null || entry is null)
        {
            return Results.NotFound();
        }
        if (request.Quantity is < 1 or > 99)
        {
            return Problem("quantity", "Quantity must be 1-99.");
        }

        entry.Quantity = request.Quantity;
        if (request.SelectedOptionIds is not null)
        {
            var sheet = await LoadSheet(db, entry.DatasheetId, list.GameSystemKey, ct);
            entry.SelectedOptions = ResolveOptions(sheet?.Options ?? [], request.SelectedOptionIds);
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> DeleteEntry(
        Guid id, Guid entryId, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        var entry = list?.Entries.FirstOrDefault(e => e.Id == entryId);
        if (list is null || entry is null)
        {
            return Results.NotFound();
        }

        list.Entries.Remove(entry);
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static Task<ArmyList?> LoadOwned(BattleLibraryDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.ArmyLists.Include(l => l.Entries).FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct);

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static ArmyListDto ToDto(ArmyList list)
    {
        var entries = list.Entries
            .OrderBy(e => e.DatasheetName)
            .Select(e =>
            {
                var optionsPoints = e.SelectedOptions.Sum(o => o.Points);
                return new ListEntryDto(
                    e.Id, e.DatasheetId, e.DatasheetName, e.Points, optionsPoints, e.Quantity,
                    (e.Points + optionsPoints) * e.Quantity,
                    e.SelectedOptions.Select(o => new SelectedOptionDto(o.Id, o.Name, o.Points)).ToList());
            })
            .ToList();
        var total = entries.Sum(e => e.LineTotal);
        var overLimit = list.PointsLimit.HasValue && total > list.PointsLimit.Value;
        return new ArmyListDto(
            list.Id, list.Name, list.GameSystemKey, list.FactionId, list.FactionName,
            list.PointsLimit, total, overLimit, list.UpdatedAt, entries);
    }

    private static int LineTotal(ListEntry e) =>
        (e.Points + e.SelectedOptions.Sum(o => o.Points)) * e.Quantity;

    private sealed record SheetInfo(string Name, int Points, IReadOnlyList<DatasheetOption> Options);

    // Pulls name/points/options for a datasheet from the cached slim jsonb.
    private static async Task<SheetInfo?> LoadSheet(
        BattleLibraryDbContext db, string datasheetId, string gameSystemKey, CancellationToken ct)
    {
        var json = await db.CachedDatasheets
            .Where(d => d.BsDataId == datasheetId && d.Faction.GameSystemKey == gameSystemKey)
            .Where(d => db.CachedGameSystems.Any(s => s.Key == gameSystemKey && s.Enabled))
            .Select(d => d.ModelJson)
            .FirstOrDefaultAsync(ct);
        if (json is null)
        {
            return null;
        }
        var sheet = JsonSerializer.Deserialize<Datasheet>(json, SlimJson);
        return sheet is null ? null : new SheetInfo(sheet.Name, sheet.Points ?? 0, sheet.Options);
    }

    // Snapshots the chosen options (name + points) against the datasheet's
    // offered options; unknown ids are ignored.
    private static List<SelectedOption> ResolveOptions(
        IReadOnlyList<DatasheetOption> available, IReadOnlyList<string>? selectedIds)
    {
        if (selectedIds is null || selectedIds.Count == 0)
        {
            return [];
        }
        var byId = available.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        return selectedIds
            .Distinct()
            .Where(byId.ContainsKey)
            .Select(id => new SelectedOption { Id = byId[id].Id, Name = byId[id].Name, Points = byId[id].Points })
            .ToList();
    }

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
