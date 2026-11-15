using System.Security.Claims;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Lists;

// Freeform army lists: every field is typed by the owner and none is checked
// against game rules. Text is trimmed and clipped to the column lengths.
public static partial class ListEndpoints
{
    private const string DefaultListName = "Untitled list";
    private const string DefaultEntryName = "Unnamed unit";
    private const string DefaultForceName = "Main";
    private const int MaxEntries = 300;
    private const int MaxForces = 20;

    public static RouteGroupBuilder MapListEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/lists").RequireAuthorization();

        group.MapGet("", GetLists);
        group.MapPost("", CreateList);
        group.MapGet("/{id:guid}", GetList);
        group.MapPut("/{id:guid}", UpdateList);
        group.MapPost("/{id:guid}/duplicate", DuplicateList);
        group.MapDelete("/{id:guid}", DeleteList);
        group.MapPost("/{id:guid}/featured", SetFeatured);
        group.MapPost("/{id:guid}/forces", AddForce);
        group.MapPut("/{id:guid}/forces/{forceId:guid}", RenameForce);
        group.MapDelete("/{id:guid}/forces/{forceId:guid}", RemoveForce);
        group.MapPost("/{id:guid}/entries", AddEntry);
        group.MapPost("/{id:guid}/entries/order", ReorderEntries);
        group.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntry);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/warlord", SetWarlord);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/attach", AttachEntry);
        group.MapDelete("/{id:guid}/entries/{entryId:guid}", DeleteEntry);

        return group;
    }

    internal static async Task<IResult> GetLists(ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        return Results.Ok(lists.Select(l => new ArmyListSummaryDto(
            l.Id, l.Name, l.GameSystemKey, l.FactionName, l.PointsLimit,
            Total(l), l.Entries.Count, l.Featured, l.UpdatedAt)));
    }

    private static async Task<IResult> CreateList(
        ListFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var list = new ArmyList { UserId = userId };
        ApplyListFields(list, request);
        list.Forces.Add(new ListForce { Name = DefaultForceName, SortOrder = 0 });
        db.ArmyLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lists/{list.Id}", ToDto(list));
    }

    internal static async Task<IResult> GetList(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        return list is null ? Results.NotFound() : Results.Ok(ToDto(list));
    }

    private static async Task<IResult> UpdateList(
        Guid id, ListFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        ApplyListFields(list, request);
        return await Save(db, list, ct);
    }

    private static async Task<IResult> DuplicateList(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var source = await LoadOwned(db, id, userId, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        // Source-id -> copy maps, so entries and attachments point at the copies.
        var forceBySource = source.Forces.ToDictionary(
            f => f.Id,
            f => new ListForce { Name = f.Name, SortOrder = f.SortOrder });
        var entryBySource = source.Entries.ToDictionary(
            e => e.Id,
            e => new ListEntry
            {
                ListForceId = forceBySource.TryGetValue(e.ListForceId, out var f) ? f.Id : e.ListForceId,
                Name = e.Name,
                Points = e.Points,
                ModelCount = e.ModelCount,
                Wargear = e.Wargear,
                Enhancement = e.Enhancement,
                Notes = e.Notes,
                SortOrder = e.SortOrder,
                IsWarlord = e.IsWarlord,
            });
        foreach (var e in source.Entries)
        {
            if (e.AttachedToEntryId is { } target && entryBySource.TryGetValue(target, out var copyTarget))
            {
                entryBySource[e.Id].AttachedToEntryId = copyTarget.Id;
            }
        }

        var copy = new ArmyList
        {
            UserId = userId,
            Name = Clip($"{source.Name} (copy)", 120)!,
            GameSystemKey = source.GameSystemKey,
            FactionName = source.FactionName,
            Detachment = source.Detachment,
            PointsLimit = source.PointsLimit,
            Notes = source.Notes,
            Forces = [.. forceBySource.Values],
            Entries = [.. entryBySource.Values],
        };
        db.ArmyLists.Add(copy);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lists/{copy.Id}", ToDto(copy));
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

    private static async Task<IResult> SetFeatured(
        Guid id, SetFeaturedRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        list.Featured = request.Featured;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> AddForce(
        Guid id, ForceFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        if (list.Forces.Count >= MaxForces)
        {
            return Problem("force", $"A list can have at most {MaxForces} sections.");
        }
        list.Forces.Add(new ListForce
        {
            ArmyListId = list.Id,
            Name = Clip(request.Name, 200) ?? $"Section {list.Forces.Count + 1}",
            SortOrder = list.Forces.Count == 0 ? 0 : list.Forces.Max(f => f.SortOrder) + 1,
        });
        return await Save(db, list, ct);
    }

    private static async Task<IResult> RenameForce(
        Guid id, Guid forceId, ForceFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        var force = list?.Forces.FirstOrDefault(f => f.Id == forceId);
        if (list is null || force is null)
        {
            return Results.NotFound();
        }
        force.Name = Clip(request.Name, 200) ?? force.Name;
        return await Save(db, list, ct);
    }

    private static async Task<IResult> RemoveForce(
        Guid id, Guid forceId, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var list = await LoadOwned(db, id, userId, ct);
        var force = list?.Forces.FirstOrDefault(f => f.Id == forceId);
        if (list is null || force is null)
        {
            return Results.NotFound();
        }
        if (list.Forces.Count <= 1)
        {
            return Problem("force", "A list must keep at least one section.");
        }

        foreach (var e in list.Entries.Where(e => e.ListForceId == forceId).ToList())
        {
            RemoveEntry(list, e);
        }
        list.Forces.Remove(force);
        return await Save(db, list, ct);
    }

    private static async Task<IResult> AddEntry(
        Guid id, EntryFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        if (list.Entries.Count >= MaxEntries)
        {
            return Problem("entries", $"A list can have at most {MaxEntries} units.");
        }

        var entry = new ListEntry
        {
            ArmyListId = list.Id,
            ListForceId = PrimaryForce(list).Id,
            SortOrder = list.Entries.Count == 0 ? 0 : list.Entries.Max(e => e.SortOrder) + 1,
        };
        if (ApplyEntryFields(list, entry, request) is { } problem)
        {
            return problem;
        }
        list.Entries.Add(entry);
        return await Save(db, list, ct);
    }

    private static async Task<IResult> UpdateEntry(
        Guid id, Guid entryId, EntryFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        if (ApplyEntryFields(list, entry, request) is { } problem)
        {
            return problem;
        }
        return await Save(db, list, ct);
    }

    // Applies the owner's custom entry ordering: SortOrder follows the given id
    // sequence; entries not listed keep a stable order after those listed.
    private static async Task<IResult> ReorderEntries(
        Guid id, SetEntryOrderRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        var position = (request.EntryIds ?? [])
            .Distinct()
            .Select((entryId, index) => (entryId, index))
            .ToDictionary(x => x.entryId, x => x.index);
        var next = position.Count;
        foreach (var e in Ordered(list.Entries))
        {
            e.SortOrder = position.TryGetValue(e.Id, out var p) ? p : next++;
        }
        return await Save(db, list, ct);
    }

    // One warlord per list: marking an entry clears the others.
    private static async Task<IResult> SetWarlord(
        Guid id, Guid entryId, SetWarlordRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        foreach (var e in list.Entries)
        {
            e.IsWarlord = request.IsWarlord && e.Id == entryId;
        }
        return await Save(db, list, ct);
    }

    // Attaches one entry to another (one level deep, so groups stay simple). A
    // null target detaches.
    private static async Task<IResult> AttachEntry(
        Guid id, Guid entryId, AttachEntryRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        if (request.AttachToEntryId is { } targetId)
        {
            var target = list.Entries.FirstOrDefault(e => e.Id == targetId);
            if (target is null || target.Id == entry.Id)
            {
                return Problem("attach", "Choose another unit in this list.");
            }
            if (target.AttachedToEntryId is not null || list.Entries.Any(e => e.AttachedToEntryId == entry.Id))
            {
                return Problem("attach", "Attached units can't be nested.");
            }
            entry.AttachedToEntryId = target.Id;
            entry.ListForceId = target.ListForceId;
        }
        else
        {
            entry.AttachedToEntryId = null;
        }
        return await Save(db, list, ct);
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
        RemoveEntry(list, entry);
        return await Save(db, list, ct);
    }

    // ---- helpers ----

    private static void ApplyListFields(ArmyList list, ListFieldsRequest request)
    {
        list.Name = Clip(request.Name, 120) ?? DefaultListName;
        list.GameSystemKey = GameSystems.Normalize(request.GameSystemKey);
        list.FactionName = Clip(request.FactionName, 200) ?? string.Empty;
        list.Detachment = Clip(request.Detachment, 200);
        list.PointsLimit = request.PointsLimit;
        list.Notes = Clip(request.Notes, 4000);
    }

    private static IResult? ApplyEntryFields(ArmyList list, ListEntry entry, EntryFieldsRequest request)
    {
        if (request.ForceId is { } forceId)
        {
            if (list.Forces.All(f => f.Id != forceId))
            {
                return Problem("force", "That section isn't part of this list.");
            }
            entry.ListForceId = forceId;
            // Units attached to this one follow it to the new section.
            foreach (var child in list.Entries.Where(e => e.AttachedToEntryId == entry.Id))
            {
                child.ListForceId = forceId;
            }
        }
        entry.Name = Clip(request.Name, 200) ?? DefaultEntryName;
        entry.Points = request.Points;
        entry.ModelCount = request.ModelCount;
        entry.Wargear = Clip(request.Wargear, 2000);
        entry.Enhancement = Clip(request.Enhancement, 200);
        entry.Notes = Clip(request.Notes, 2000);
        return null;
    }

    private static void RemoveEntry(ArmyList list, ListEntry entry)
    {
        foreach (var child in list.Entries.Where(e => e.AttachedToEntryId == entry.Id))
        {
            child.AttachedToEntryId = null;
        }
        list.Entries.Remove(entry);
    }

    // The primary section, created if (defensively) none exists.
    private static ListForce PrimaryForce(ArmyList list)
    {
        var existing = list.Forces.OrderBy(f => f.SortOrder).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }
        var force = new ListForce { ArmyListId = list.Id, Name = DefaultForceName, SortOrder = 0 };
        list.Forces.Add(force);
        return force;
    }

    private static async Task<IResult> Save(BattleLibraryDbContext db, ArmyList list, CancellationToken ct)
    {
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    // Trimmed text clipped to max; null when blank.
    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static IOrderedEnumerable<ListEntry> Ordered(IEnumerable<ListEntry> entries) =>
        entries.OrderBy(e => e.SortOrder).ThenBy(e => e.CreatedAt);

    internal static int Total(ArmyList list) => list.Entries.Sum(e => e.Points ?? 0);

    private static Task<ArmyList?> LoadOwned(BattleLibraryDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.ArmyLists.Include(l => l.Entries).Include(l => l.Forces)
            .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct);

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    internal static ArmyListDto ToDto(ArmyList list)
    {
        var entries = Ordered(list.Entries)
            .Select(e => new ListEntryDto(
                e.Id, e.ListForceId, e.Name, e.Points, e.ModelCount, e.Wargear, e.Enhancement, e.Notes,
                e.SortOrder, e.IsWarlord, e.AttachedToEntryId))
            .ToList();
        var forces = list.Forces
            .OrderBy(f => f.SortOrder)
            .Select(f => new ListForceDto(f.Id, f.Name, f.SortOrder))
            .ToList();
        return new ArmyListDto(
            list.Id, list.Name, list.GameSystemKey, list.FactionName, list.Detachment, list.PointsLimit, list.Notes,
            Total(list), list.Featured, forces, list.UpdatedAt, entries);
    }

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
