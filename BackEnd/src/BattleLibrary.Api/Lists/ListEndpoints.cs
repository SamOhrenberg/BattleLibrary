using System.Security.Claims;
using System.Text.Json;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.Persistence;
using BattleLibrary.Infrastructure.Rosters;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Lists;

public static partial class ListEndpoints
{
    // Cached datasheet jsonb is serialized with web defaults (camelCase).
    private static readonly JsonSerializerOptions SlimJson = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapListEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/lists").RequireAuthorization();

        group.MapGet("", GetLists);
        group.MapPost("", CreateList);
        group.MapPost("/import", ImportRoster).DisableAntiforgery();
        group.MapGet("/{id:guid}/export", ExportRoster);
        group.MapGet("/{id:guid}", GetList);
        group.MapPut("/{id:guid}", UpdateList);
        group.MapPost("/{id:guid}/duplicate", DuplicateList);
        group.MapDelete("/{id:guid}", DeleteList);
        group.MapPost("/{id:guid}/featured", SetFeatured);
        group.MapPost("/{id:guid}/battle-size", SetBattleSize);
        group.MapPost("/{id:guid}/disposition", SetDisposition);
        group.MapPost("/{id:guid}/detachments", SetDetachments);
        group.MapPost("/{id:guid}/forces", AddForce);
        group.MapDelete("/{id:guid}/forces/{forceId:guid}", RemoveForce);
        group.MapPost("/{id:guid}/entries", AddEntry);
        group.MapPost("/{id:guid}/entries/order", ReorderEntries);
        group.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntry);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/size", SetModelCount);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/warlord", SetWarlord);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/attach", AttachEntry);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/enhancement", SetEnhancement);
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
            l.Featured,
            l.BattleSize,
            l.Entries.Any(EntryIsLegends),
            l.Entries.Any(EntryIsCrucible),
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

        // A battle size (if given) sets the points limit from its cap; "Custom"
        // or none keeps the manually supplied limit.
        string? battleSize = null;
        var pointsLimit = request.PointsLimit;
        var size = request.BattleSize?.Trim();
        if (!string.IsNullOrEmpty(size) && !string.Equals(size, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            if (!BattleSizes.TryGetValue(size, out var cap))
            {
                return Problem("battleSize", "Unknown battle size.");
            }
            battleSize = BattleSizes.Keys.First(k => string.Equals(k, size, StringComparison.OrdinalIgnoreCase));
            pointsLimit = cap;
        }

        var list = new ArmyList
        {
            UserId = userId,
            Name = name,
            GameSystemKey = request.GameSystemKey,
            FactionId = request.FactionId,
            FactionName = factionName,
            PointsLimit = pointsLimit,
            BattleSize = battleSize,
        };
        // Every list starts with one primary force, mirroring its faction.
        list.Forces.Add(new ListForce { FactionId = request.FactionId, FactionName = factionName, SortOrder = 0 });
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

        // Keep room for the " (copy)" suffix within the 120-char name limit.
        var baseName = source.Name.Length > 113 ? source.Name[..113] : source.Name;

        // Copy forces, keeping a source-id -> new-force map so entries can be
        // reassigned to the copies.
        var forceBySource = new Dictionary<Guid, ListForce>();
        foreach (var f in source.Forces)
        {
            forceBySource[f.Id] = new ListForce
            {
                FactionId = f.FactionId,
                FactionName = f.FactionName,
                SortOrder = f.SortOrder,
            };
        }

        // Copy entries, keeping a source-id -> new-entry map so Leader attachments
        // can be rewired to the copies' new ids.
        var entryBySource = new Dictionary<Guid, ListEntry>();
        foreach (var e in source.Entries)
        {
            entryBySource[e.Id] = new ListEntry
            {
                DatasheetId = e.DatasheetId,
                DatasheetName = e.DatasheetName,
                CustomName = e.CustomName,
                Notes = e.Notes,
                Points = e.Points,
                ModelCount = e.ModelCount,
                Quantity = e.Quantity,
                SortOrder = e.SortOrder,
                Keywords = e.Keywords.ToList(),
                IsWarlord = e.IsWarlord,
                EnhancementId = e.EnhancementId,
                EnhancementName = e.EnhancementName,
                EnhancementPoints = e.EnhancementPoints,
                SelectedOptions = e.SelectedOptions
                    .Select(o => new SelectedOption { Id = o.Id, Name = o.Name, Points = o.Points, Quantity = o.Quantity })
                    .ToList(),
            };
            if (forceBySource.TryGetValue(e.ListForceId, out var copyForce))
            {
                entryBySource[e.Id].ListForceId = copyForce.Id;
            }
        }
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
            Name = $"{baseName} (copy)",
            GameSystemKey = source.GameSystemKey,
            FactionId = source.FactionId,
            FactionName = source.FactionName,
            PointsLimit = source.PointsLimit,
            BattleSize = source.BattleSize,
            ForceDisposition = source.ForceDisposition,
            Featured = false,
            Detachments = source.Detachments
                .Select(d => new ListDetachment { DetachmentId = d.DetachmentId, Name = d.Name, Dp = d.Dp })
                .ToList(),
            Forces = source.Forces.Select(f => forceBySource[f.Id]).ToList(),
            Entries = source.Entries.Select(e => entryBySource[e.Id]).ToList(),
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

    // 40k battle sizes and their points caps.
    private static readonly Dictionary<string, int> BattleSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Incursion"] = 1000,
        ["Strike Force"] = 2000,
        ["Onslaught"] = 3000,
    };

    private static async Task<IResult> SetBattleSize(
        Guid id, SetBattleSizeRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        var size = request.BattleSize?.Trim();
        if (string.IsNullOrEmpty(size) || string.Equals(size, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            list.BattleSize = null;
        }
        else if (BattleSizes.TryGetValue(size, out var cap))
        {
            list.BattleSize = BattleSizes.Keys.First(k => string.Equals(k, size, StringComparison.OrdinalIgnoreCase));
            list.PointsLimit = cap;
        }
        else
        {
            return Problem("battleSize", "Unknown battle size.");
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    // Returns the list's primary force, creating one from the list's faction if
    // (defensively) none exists yet.
    private static ListForce PrimaryForce(ArmyList list)
    {
        var existing = list.Forces.OrderBy(f => f.SortOrder).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }
        var force = new ListForce { ArmyListId = list.Id, FactionId = list.FactionId, FactionName = list.FactionName, SortOrder = 0 };
        list.Forces.Add(force);
        return force;
    }

    private static async Task<IResult> AddForce(
        Guid id, AddForceRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        if (string.IsNullOrWhiteSpace(request.FactionId))
        {
            return Problem("faction", "A faction is required.");
        }

        var factionName = await db.CachedFactions
            .Where(f => f.BsDataId == request.FactionId && f.GameSystemKey == list.GameSystemKey)
            .Where(f => db.CachedGameSystems.Any(s => s.Key == list.GameSystemKey && s.Enabled))
            .Select(f => f.Name)
            .FirstOrDefaultAsync(ct);
        if (factionName is null)
        {
            return Results.NotFound(new { error = "Unknown faction for this list's game system." });
        }

        PrimaryForce(list); // ensure the list has its primary force before appending
        var nextOrder = list.Forces.Count == 0 ? 0 : list.Forces.Max(f => f.SortOrder) + 1;
        list.Forces.Add(new ListForce
        {
            ArmyListId = list.Id,
            FactionId = request.FactionId,
            FactionName = factionName,
            SortOrder = nextOrder,
        });
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> RemoveForce(
        Guid id, Guid forceId, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        var force = list.Forces.FirstOrDefault(f => f.Id == forceId);
        if (force is null)
        {
            return Results.NotFound();
        }
        if (list.Forces.Count <= 1)
        {
            return Problem("force", "A list must keep at least one force.");
        }

        // Remove the force and every entry assigned to it.
        var removed = list.Entries.Where(e => e.ListForceId == forceId).ToList();
        foreach (var e in removed)
        {
            // Detach any Leaders that were attached to a removed unit.
            foreach (var leader in list.Entries.Where(l => l.AttachedToEntryId == e.Id))
            {
                leader.AttachedToEntryId = null;
            }
            list.Entries.Remove(e);
        }
        list.Forces.Remove(force);
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> SetDisposition(
        Guid id, SetDispositionRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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
        var value = request.ForceDisposition?.Trim();
        if (value is { Length: > 100 })
        {
            return Problem("forceDisposition", "Force disposition must be 100 characters or fewer.");
        }
        list.ForceDisposition = string.IsNullOrEmpty(value) ? null : value;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> SetDetachments(
        Guid id, SetDetachmentsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        var available = await LoadDetachments(db, list.GameSystemKey, list.FactionId, ct);
        var chosen = (request.DetachmentIds ?? [])
            .Distinct()
            .Select(did => available.FirstOrDefault(d => d.Id == did))
            .OfType<DetachmentInfo>()
            .ToList();

        list.Detachments = chosen
            .Select(d => new ListDetachment { DetachmentId = d.Id, Name = d.Name, Dp = d.Dp })
            .ToList();

        // Drop enhancements whose detachment is no longer chosen.
        var validEnhancementIds = chosen.SelectMany(d => d.Enhancements).Select(e => e.Id).ToHashSet();
        foreach (var e in list.Entries.Where(e => e.EnhancementId is not null && !validEnhancementIds.Contains(e.EnhancementId)))
        {
            e.EnhancementId = null;
            e.EnhancementName = null;
            e.EnhancementPoints = 0;
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

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

        // A unit whose rules force it to be the Warlord can't give up the slot,
        // and no other unit can take it while such a unit is in the list.
        if (entry.ForcedWarlord && !request.IsWarlord)
        {
            return Problem("warlord", "This unit's rules require it to be your Warlord.");
        }
        if (request.IsWarlord && !entry.ForcedWarlord && list.Entries.Any(e => e.ForcedWarlord))
        {
            return Problem("warlord", "A unit whose rules require it to be the Warlord is already in this list.");
        }

        if (request.IsWarlord)
        {
            if (!HasKeyword(entry, "Character"))
            {
                return Problem("warlord", "Only a Character can be the Warlord.");
            }
            // Clear other non-forced Warlords; a forced Warlord keeps the slot.
            foreach (var e in list.Entries)
            {
                e.IsWarlord = e.Id == entryId || e.ForcedWarlord;
            }
        }
        else
        {
            entry.IsWarlord = false;
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    // Attaches a Leader (a Character) to a Bodyguard (a non-Character unit) so the
    // two are grouped as one attached unit. A null target detaches.
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

        if (request.AttachToEntryId is null)
        {
            entry.AttachedToEntryId = null;
            list.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(list));
        }

        if (!HasKeyword(entry, "Character"))
        {
            return Problem("attach", "Only a Character (Leader) can be attached to a unit.");
        }
        var target = list.Entries.FirstOrDefault(e => e.Id == request.AttachToEntryId);
        if (target is null || target.Id == entry.Id)
        {
            return Problem("attach", "Choose a unit in this list to attach to.");
        }
        if (HasKeyword(target, "Character"))
        {
            return Problem("attach", "A Leader attaches to a non-Character unit, not another Character.");
        }

        // Data-driven leader attachment: when the Leader's datasheet declares the
        // units it can lead (parsed from BSData associations), the target must be
        // one of them - matched by the target unit's keywords, which carry its
        // datasheet category. A Leader with no declared targets (older data, or a
        // system without the mechanic) is left unrestricted.
        var leaderSheet = await LoadSheet(db, entry.DatasheetId, list.GameSystemKey, ct);
        var targetNames = leaderSheet?.LeaderAttachments
            .SelectMany(a => a.Targets)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targetNames is { Count: > 0 } && !target.Keywords.Any(targetNames.Contains))
        {
            return Problem("attach", $"{entry.DatasheetName} can't lead {target.DatasheetName}.");
        }

        entry.AttachedToEntryId = target.Id;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> SetEnhancement(
        Guid id, Guid entryId, SetEnhancementRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.EnhancementId))
        {
            entry.EnhancementId = null;
            entry.EnhancementName = null;
            entry.EnhancementPoints = 0;
        }
        else
        {
            if (list.Detachments.Count == 0)
            {
                return Problem("enhancement", "Choose a detachment before assigning enhancements.");
            }
            if (!HasKeyword(entry, "Character") || HasKeyword(entry, "Epic Hero"))
            {
                return Problem("enhancement", "Enhancements can only go on a non-Epic Hero Character.");
            }
            if (list.Entries.Any(e => e.Id != entryId && e.EnhancementId == request.EnhancementId))
            {
                return Problem("enhancement", "That enhancement is already taken.");
            }

            var detachments = await LoadDetachments(db, list.GameSystemKey, list.FactionId, ct);
            var chosenIds = list.Detachments.Select(d => d.DetachmentId).ToHashSet();
            var enhancement = detachments
                .Where(d => chosenIds.Contains(d.Id))
                .SelectMany(d => d.Enhancements)
                .FirstOrDefault(x => x.Id == request.EnhancementId);
            if (enhancement is null)
            {
                return Results.NotFound(new { error = "That enhancement isn't in your chosen detachments." });
            }
            entry.EnhancementId = enhancement.Id;
            entry.EnhancementName = enhancement.Name;
            entry.EnhancementPoints = enhancement.Points;
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
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

        var sheet = await LoadSheet(db, request.DatasheetId, list.GameSystemKey, ct);
        if (sheet is null)
        {
            return Results.NotFound(new { error = "Unknown datasheet for this list's game system." });
        }

        // The force the unit joins: the requested one, else the primary force.
        var force = request.ForceId is { } fid
            ? list.Forces.FirstOrDefault(f => f.Id == fid)
            : PrimaryForce(list);
        if (force is null)
        {
            return Problem("force", "That force isn't part of this list.");
        }

        // One entry per unit; multiple units of a datasheet are separate entries.
        var size = PickSize(sheet.Sizes, request.Models);
        var entry = new ListEntry
        {
            ArmyListId = list.Id,
            ListForceId = force.Id,
            DatasheetId = request.DatasheetId,
            DatasheetName = sheet.Name,
            Points = size?.Points ?? sheet.Points,
            ModelCount = size?.Models ?? 1,
            Keywords = sheet.Keywords.ToList(),
            ForcedWarlord = sheet.ForcedWarlord,
            SortOrder = list.Entries.Count == 0 ? 0 : list.Entries.Max(e => e.SortOrder) + 1,
            SelectedOptions =
            [
                .. ResolveOptions(sheet.Options, request.SelectedOptionIds),
                .. ResolveVariants(sheet.VariantGroups, request.Variants),
            ],
        };
        entry.DynamicPoints = DynamicPoints(sheet.CostRules, entry.SelectedOptions);
        list.Entries.Add(entry);

        // An Epic Hero whose rules mandate it takes the Warlord slot outright.
        // Other non-forced Warlords are cleared; any existing forced one stays
        // (two forced Warlords is flagged as illegal by validation).
        if (sheet.ForcedWarlord)
        {
            entry.IsWarlord = true;
            foreach (var e in list.Entries)
            {
                if (e.Id != entry.Id && !e.ForcedWarlord)
                {
                    e.IsWarlord = false;
                }
            }
        }
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
        // Options and variants are independent slices of SelectedOptions: a null
        // field leaves its slice as is, so wargear edits and loadout edits can be
        // sent separately.
        if (request.SelectedOptionIds is not null || request.Variants is not null)
        {
            var sheet = await LoadSheet(db, entry.DatasheetId, list.GameSystemKey, ct);
            var groups = sheet?.VariantGroups ?? [];
            var plain = request.SelectedOptionIds is not null
                ? ResolveOptions(sheet?.Options ?? [], request.SelectedOptionIds)
                : NonVariantOptions(entry, groups);
            var chosenVariants = request.Variants is not null
                ? ResolveVariants(groups, request.Variants)
                : VariantOptions(entry, groups);
            entry.SelectedOptions = [.. plain, .. chosenVariants];
            entry.DynamicPoints = DynamicPoints(sheet?.CostRules ?? [], entry.SelectedOptions);
        }
        // A null field is left as is; a sent value (including "") updates, with
        // blank clearing the value. Lengths match the column limits.
        if (request.CustomName is not null)
        {
            var name = request.CustomName.Trim();
            if (name.Length > 200)
            {
                return Problem("customName", "Custom name must be 200 characters or fewer.");
            }
            entry.CustomName = name.Length == 0 ? null : name;
        }
        if (request.Notes is not null)
        {
            var notes = request.Notes.Trim();
            if (notes.Length > 2000)
            {
                return Problem("notes", "Notes must be 2000 characters or fewer.");
            }
            entry.Notes = notes.Length == 0 ? null : notes;
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    // Applies the owner's custom entry ordering: SortOrder follows the given id
    // sequence; any entries not listed keep a stable order after those listed.
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
            .Select((entryId, index) => (entryId, index))
            .ToDictionary(x => x.entryId, x => x.index);
        var next = request.EntryIds?.Count ?? 0;
        foreach (var e in list.Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.DatasheetName))
        {
            e.SortOrder = position.TryGetValue(e.Id, out var p) ? p : next++;
        }
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    private static async Task<IResult> SetModelCount(
        Guid id, Guid entryId, SetModelCountRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        var sheet = await LoadSheet(db, entry.DatasheetId, list.GameSystemKey, ct);
        var size = sheet?.Sizes.FirstOrDefault(s => s.Models == request.Models);
        if (size is null)
        {
            return Problem("models", "That isn't a valid size for this unit.");
        }
        entry.ModelCount = size.Models;
        entry.Points = size.Points;
        // A new size re-fills the loadout pool: keep the wargear, replace variants.
        if (request.Variants is not null)
        {
            var groups = sheet!.VariantGroups;
            entry.SelectedOptions = [.. NonVariantOptions(entry, groups), .. ResolveVariants(groups, request.Variants)];
        }
        entry.DynamicPoints = DynamicPoints(sheet.CostRules, entry.SelectedOptions);
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

        // Detach any Leaders attached to the unit being removed.
        foreach (var leader in list.Entries.Where(e => e.AttachedToEntryId == entry.Id))
        {
            leader.AttachedToEntryId = null;
        }
        list.Entries.Remove(entry);
        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(list));
    }

    // Exports the list as a BattleScribe .rosz (zipped roster), referencing the
    // stable BSData ids so it opens in BattleScribe / NewRecruit.
    private static async Task<IResult> ExportRoster(Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
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

        var header = await db.CachedGameSystems
            .Where(s => s.Key == list.GameSystemKey && s.Enabled)
            .Select(s => new { s.BsDataId, s.BsDataName, s.Revision, s.PointsCostTypeId, s.ForceEntryId, s.ForceEntryName })
            .FirstOrDefaultAsync(ct);
        if (header is null)
        {
            return Results.NotFound(new { error = "This list's game system is no longer available." });
        }
        var revisionByFaction = await db.CachedFactions
            .Where(f => f.GameSystemKey == list.GameSystemKey)
            .Select(f => new { f.BsDataId, f.Revision })
            .ToDictionaryAsync(f => f.BsDataId, f => f.Revision, ct);

        static RosterSelection ToSelection(ListEntry e)
        {
            var children = e.SelectedOptions
                .Select(o => new RosterSelection(o.Id, o.Name, Math.Max(1, o.Quantity), "upgrade", o.Points * Math.Max(1, o.Quantity), []))
                .ToList();
            if (e.EnhancementId is not null)
            {
                children.Add(new RosterSelection(e.EnhancementId, e.EnhancementName ?? "", 1, "upgrade", e.EnhancementPoints, []));
            }
            return new RosterSelection(e.DatasheetId, e.DatasheetName, Math.Max(1, e.Quantity), "unit", e.Points, children, e.CustomName);
        }

        PrimaryForce(list); // ensure at least one force exists
        var orderedForces = list.Forces.OrderBy(f => f.SortOrder).ToList();
        var primaryForceId = orderedForces[0].Id;

        // One roster <force> per ListForce; entries bucket by ListForceId, and the
        // army's detachments ride on the primary force.
        var rosterForces = new List<RosterForce>();
        foreach (var f in orderedForces)
        {
            var selections = list.Entries
                .Where(e => e.ListForceId == f.Id)
                .Select(ToSelection)
                .ToList();
            if (f.Id == primaryForceId)
            {
                selections.AddRange(list.Detachments
                    .Select(d => new RosterSelection(d.DetachmentId, d.Name, 1, "upgrade", 0, [])));
            }
            revisionByFaction.TryGetValue(f.FactionId, out var revision);
            rosterForces.Add(new RosterForce(
                f.FactionId, f.FactionName, revision, selections,
                header.ForceEntryId, header.ForceEntryName));
        }
        // Any entry not matched to a force (defensive) rides on the primary force.
        var orphanSelections = list.Entries
            .Where(e => orderedForces.All(f => f.Id != e.ListForceId))
            .Select(ToSelection)
            .ToList();
        if (orphanSelections.Count > 0)
        {
            rosterForces[0] = rosterForces[0] with { Selections = [.. rosterForces[0].Selections, .. orphanSelections] };
        }

        var gameSystemName = string.IsNullOrEmpty(header.BsDataName) ? list.FactionName : header.BsDataName;
        var roster = new RosterDocument(
            list.Name, header.BsDataId, gameSystemName, header.Revision, header.PointsCostTypeId, rosterForces);

        var baseName = SafeFileName(list.Name);
        var bytes = RosterXml.Zip(baseName, RosterXml.Write(roster));
        return Results.File(bytes, "application/octet-stream", $"{baseName}.rosz");
    }

    // Imports a BattleScribe roster (.ros or .rosz) into a new list, matching each
    // selection's source entry id against the cached catalogue. Selections with no
    // match are reported, not dropped silently.
    private static async Task<IResult> ImportRoster(
        IFormFile? file, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        if (file is null || file.Length == 0)
        {
            return Problem("file", "Upload a .ros or .rosz roster file.");
        }
        if (file.Length > 5 * 1024 * 1024)
        {
            return Problem("file", "Roster file is too large (max 5 MB).");
        }

        RosterDocument roster;
        try
        {
            await using var stream = file.OpenReadStream();
            roster = RosterXml.Read(stream);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or InvalidDataException)
        {
            return Problem("file", "That file isn't a readable BattleScribe roster.");
        }

        var systemKey = await ResolveSystemKey(db, roster, ct);
        if (systemKey is null)
        {
            return Problem("file", "This roster's game system isn't one Battle Library has loaded.");
        }

        // Match each roster force's catalogue to a known faction, in order. Forces
        // whose catalogue isn't loaded are skipped; the first match is primary.
        var forceFactions = new List<(RosterForce RForce, string FactionId, string FactionName)>();
        foreach (var f in roster.Forces)
        {
            var match = await db.CachedFactions
                .Where(cf => cf.GameSystemKey == systemKey && cf.BsDataId == f.CatalogueId)
                .Select(cf => new { cf.BsDataId, cf.Name })
                .FirstOrDefaultAsync(ct);
            if (match is not null)
            {
                forceFactions.Add((f, match.BsDataId, match.Name));
            }
        }
        if (forceFactions.Count == 0)
        {
            return Problem("file", "Couldn't match this roster's catalogue to a known faction.");
        }

        var primaryFactionId = forceFactions[0].FactionId;
        var list = new ArmyList
        {
            UserId = userId,
            Name = ClampName(roster.Name),
            GameSystemKey = systemKey,
            FactionId = primaryFactionId,
            FactionName = forceFactions[0].FactionName,
        };

        // One ListForce per distinct faction, in first-seen order.
        var forceByFaction = new Dictionary<string, ListForce>();
        var forceOrder = 0;
        foreach (var ff in forceFactions)
        {
            if (!forceByFaction.ContainsKey(ff.FactionId))
            {
                var lf = new ListForce
                {
                    ArmyListId = list.Id,
                    FactionId = ff.FactionId,
                    FactionName = ff.FactionName,
                    SortOrder = forceOrder++,
                };
                forceByFaction[ff.FactionId] = lf;
                list.Forces.Add(lf);
            }
        }

        // Detachments + enhancements belong to the army's primary faction.
        var detachments = await LoadDetachments(db, systemKey, primaryFactionId, ct);
        var enhancementsById = detachments.SelectMany(d => d.Enhancements)
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var detachmentsById = detachments.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var detachmentIds = detachmentsById.Keys.ToHashSet();

        // Datasheet ids per matched faction, to classify a selection without a DB
        // round-trip per node.
        var sheetIdsByFaction = new Dictionary<string, HashSet<string>>();
        foreach (var fid in forceByFaction.Keys)
        {
            sheetIdsByFaction[fid] = (await db.CachedDatasheets
                .Where(d => d.Faction.GameSystemKey == systemKey && d.Faction.BsDataId == fid)
                .Select(d => d.BsDataId)
                .ToListAsync(ct)).ToHashSet();
        }

        var unmatched = new List<string>();
        var chosenDetachments = new List<ListDetachment>();
        var unitHits = new List<(string SheetId, RosterSelection Selection, ListForce Force)>();

        // Walk the selection tree for one force. BattleScribe/NewRecruit nest units
        // among config wrappers and bury the detachment inside a "Detachment"
        // wrapper, so this recurses; a datasheet/detachment match stops the descent.
        void Walk(RosterSelection sel, HashSet<string> sheetIds, ListForce force)
        {
            if (MatchId(sel.EntryId, sheetIds) is { } sheetId)
            {
                unitHits.Add((sheetId, sel, force));
                return;
            }
            if (MatchId(sel.EntryId, detachmentIds) is { } detId)
            {
                if (chosenDetachments.All(c => c.DetachmentId != detId))
                {
                    var det = detachmentsById[detId];
                    chosenDetachments.Add(new ListDetachment { DetachmentId = det.Id, Name = det.Name, Dp = det.Dp });
                }
                return;
            }
            // An unknown unit (priced, or typed as a unit/model) - report it rather
            // than dig into its wargear. Config wrappers (battle size, disposition)
            // are unpriced upgrades: recurse into them for nested content instead.
            if (sel.Points > 0 || sel.Type is "unit" or "model")
            {
                unmatched.Add(string.IsNullOrWhiteSpace(sel.Name) ? sel.EntryId : sel.Name);
                return;
            }
            foreach (var child in sel.Selections)
            {
                Walk(child, sheetIds, force);
            }
        }
        foreach (var ff in forceFactions)
        {
            var force = forceByFaction[ff.FactionId];
            var sheetIds = sheetIdsByFaction[ff.FactionId];
            foreach (var sel in ff.RForce.Selections)
            {
                Walk(sel, sheetIds, force);
            }
        }

        var importOrder = 0;
        foreach (var (sheetId, sel, force) in unitHits)
        {
            var sheet = await LoadSheet(db, sheetId, systemKey, ct);
            if (sheet is null)
            {
                unmatched.Add(string.IsNullOrWhiteSpace(sel.Name) ? sel.EntryId : sel.Name);
                continue;
            }

            var size = PickSizeByPoints(sheet.Sizes, sel.Points);
            var entry = new ListEntry
            {
                ArmyListId = list.Id,
                ListForceId = force.Id,
                DatasheetId = sheetId,
                DatasheetName = sheet.Name,
                CustomName = NormalizeCustomName(sel.CustomName, sheet.Name),
                Points = size?.Points ?? sheet.Points,
                ModelCount = size?.Models ?? 1,
                SortOrder = importOrder++,
                Keywords = sheet.Keywords.ToList(),
                ForcedWarlord = sheet.ForcedWarlord,
            };
            var optionIds = sheet.Options.Select(o => o.Id).ToHashSet();
            var selected = new List<string>();
            foreach (var child in sel.Selections)
            {
                if (entry.EnhancementId is null && MatchId(child.EntryId, enhancementsById.Keys.ToHashSet()) is { } enhId)
                {
                    var enh = enhancementsById[enhId];
                    entry.EnhancementId = enh.Id;
                    entry.EnhancementName = enh.Name;
                    entry.EnhancementPoints = enh.Points;
                }
                else if (string.Equals(child.Name, "Warlord", StringComparison.OrdinalIgnoreCase) && HasKeyword(entry, "Character"))
                {
                    entry.IsWarlord = true;
                }
                else if (MatchId(child.EntryId, optionIds) is { } optId)
                {
                    selected.Add(optId);
                }
            }
            entry.SelectedOptions = ResolveOptions(sheet.Options, selected);
            entry.DynamicPoints = DynamicPoints(sheet.CostRules, entry.SelectedOptions);
            list.Entries.Add(entry);
        }
        list.Detachments = chosenDetachments;

        db.ArmyLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lists/{list.Id}", new ImportRosterResultDto(ToDto(list), unmatched));
    }

    // Matches the roster to a loaded system by BattleScribe gameSystemId, then by
    // game-system name, then by any force's catalogue id (so files with a blank or
    // mis-spelled system header still resolve). Returns the key, or null.
    private static async Task<string?> ResolveSystemKey(BattleLibraryDbContext db, RosterDocument roster, CancellationToken ct)
    {
        var enabled = db.CachedGameSystems.Where(s => s.Enabled);
        if (!string.IsNullOrEmpty(roster.GameSystemId))
        {
            var byId = await enabled.Where(s => s.BsDataId == roster.GameSystemId).Select(s => s.Key).FirstOrDefaultAsync(ct);
            if (byId is not null)
            {
                return byId;
            }
        }
        if (!string.IsNullOrEmpty(roster.GameSystemName))
        {
            var byName = await enabled.Where(s => s.BsDataName == roster.GameSystemName).Select(s => s.Key).FirstOrDefaultAsync(ct);
            if (byName is not null)
            {
                return byName;
            }
        }
        var catalogueIds = roster.Forces.Select(f => f.CatalogueId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (catalogueIds.Count > 0)
        {
            return await db.CachedFactions
                .Where(f => catalogueIds.Contains(f.BsDataId) && db.CachedGameSystems.Any(s => s.Key == f.GameSystemKey && s.Enabled))
                .Select(f => f.GameSystemKey)
                .FirstOrDefaultAsync(ct);
        }
        return null;
    }

    // BattleScribe/NewRecruit write entry ids as "::"-joined link paths (e.g.
    // "e2d-…::e651-…"), whose trailing segment is the shared entry id this cache
    // keys on. Match the whole id first, then each segment from the leaf inward.
    private static string? MatchId(string entryId, HashSet<string> ids)
    {
        if (string.IsNullOrEmpty(entryId))
        {
            return null;
        }
        if (ids.Contains(entryId))
        {
            return entryId;
        }
        var parts = entryId.Split("::", StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (ids.Contains(parts[i]))
            {
                return parts[i];
            }
        }
        return null;
    }

    // The size whose points match the imported selection, else the smallest.
    private static UnitSize? PickSizeByPoints(IReadOnlyList<UnitSize> sizes, int points)
    {
        if (sizes.Count == 0)
        {
            return null;
        }
        return sizes.FirstOrDefault(s => s.Points == points) ?? sizes.OrderBy(s => s.Models).First();
    }

    // A roster selection's customName, kept only when it adds something: blank or
    // equal to the datasheet name is dropped, and it's capped at the column limit.
    private static string? NormalizeCustomName(string? customName, string datasheetName)
    {
        var trimmed = customName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, datasheetName, StringComparison.Ordinal))
        {
            return null;
        }
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }

    private static string ClampName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Imported roster";
        }
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    // A filesystem-safe base name for the download; falls back when empty.
    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "roster" : cleaned;
    }

    private static Task<ArmyList?> LoadOwned(BattleLibraryDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.ArmyLists.Include(l => l.Entries).Include(l => l.Forces)
            .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct);

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static ArmyListDto ToDto(ArmyList list)
    {
        var entries = list.Entries
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.DatasheetName)
            .Select(e =>
            {
                var optionsPoints = OptionsPoints(e);
                return new ListEntryDto(
                    e.Id, e.ListForceId, e.DatasheetId, e.DatasheetName, e.CustomName, e.Notes, e.Points, optionsPoints, e.ModelCount,
                    e.SortOrder,
                    e.Points + optionsPoints + e.EnhancementPoints + e.DynamicPoints,
                    e.Keywords, e.IsWarlord, e.ForcedWarlord, e.AttachedToEntryId, e.EnhancementId, e.EnhancementName, e.EnhancementPoints,
                    e.SelectedOptions.Select(o => new SelectedOptionDto(o.Id, o.Name, o.Points, Math.Max(1, o.Quantity))).ToList(),
                    e.DynamicPoints);
            })
            .ToList();
        var total = entries.Sum(e => e.LineTotal);
        var overLimit = list.PointsLimit.HasValue && total > list.PointsLimit.Value;
        var detachments = list.Detachments
            .Select(d => new ListDetachmentDto(d.DetachmentId, d.Name, d.Dp))
            .ToList();
        var forces = list.Forces
            .OrderBy(f => f.SortOrder)
            .Select(f => new ListForceDto(f.Id, f.FactionId, f.FactionName, f.SortOrder))
            .ToList();
        return new ArmyListDto(
            list.Id, list.Name, list.GameSystemKey, list.FactionId, list.FactionName,
            list.PointsLimit, total, overLimit, list.Featured,
            list.BattleSize, list.ForceDisposition, list.Entries.Any(EntryIsLegends), list.Entries.Any(EntryIsCrucible),
            detachments, forces, list.UpdatedAt, entries);
    }

    private static int LineTotal(ListEntry e) =>
        e.Points + OptionsPoints(e) + e.EnhancementPoints + e.DynamicPoints;

    // Points from wargear and loadout variants, each multiplied by its count
    // (count is 1 for a plain upgrade).
    private static int OptionsPoints(ListEntry e) =>
        e.SelectedOptions.Sum(o => o.Points * Math.Max(1, o.Quantity));

    // Selection-dependent points from a datasheet's cost rules (Crucible warband
    // weapon pricing), given the entry's chosen options. Per-selection rules add
    // per counted selection; threshold rules add once when the count qualifies.
    private static int DynamicPoints(IReadOnlyList<CostRule> rules, IEnumerable<SelectedOption> options)
    {
        if (rules.Count == 0)
        {
            return 0;
        }
        var selected = options.ToList();
        var total = 0;
        foreach (var rule in rules)
        {
            var counted = rule.CountedOptionIds.ToHashSet(StringComparer.Ordinal);
            var count = selected.Where(o => counted.Contains(o.Id)).Sum(o => Math.Max(1, o.Quantity));
            if (rule.PerSelection)
            {
                total += rule.Amount * (count / Math.Max(1, rule.Step));
            }
            else if (Qualifies(count, rule.Operator, rule.Value))
            {
                total += rule.Amount;
            }
        }
        return total;
    }

    private static bool Qualifies(int count, string op, int value) => op switch
    {
        "atLeast" => count >= value,
        "greaterThan" => count > value,
        "atMost" => count <= value,
        "lessThan" => count < value,
        "equalTo" => count == value,
        _ => false,
    };

    private sealed record SheetInfo(
        string Name, int Points, IReadOnlyList<string> Keywords, IReadOnlyList<DatasheetOption> Options, IReadOnlyList<UnitSize> Sizes,
        bool ForcedWarlord, IReadOnlyList<ModelVariantGroup> VariantGroups, IReadOnlyList<CostRule> CostRules,
        IReadOnlyList<LeaderAttachment> LeaderAttachments);

    // Pulls name/points/keywords/options/sizes/variants for a datasheet from the cached slim jsonb.
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
        return sheet is null
            ? null
            : new SheetInfo(
                sheet.Name, sheet.Points ?? 0, sheet.Keywords, sheet.Options, sheet.Sizes ?? [], IsForcedWarlord(sheet),
                sheet.VariantGroups ?? [], sheet.CostRules ?? [], sheet.LeaderAttachments ?? []);
    }

    // An Epic Hero whose rules force it to be the army's Warlord ("...it must be
    // your Warlord."). Markup markers (** bold, ^^ keyword) are stripped first.
    private static bool IsForcedWarlord(Datasheet sheet) =>
        sheet.Abilities.Any(a =>
            !string.IsNullOrEmpty(a.Text)
            && a.Text.Replace("^", "").Replace("*", "").Contains("must be your Warlord", StringComparison.OrdinalIgnoreCase));

    // The chosen size (by model count), or the smallest when unspecified/unknown.
    private static UnitSize? PickSize(IReadOnlyList<UnitSize> sizes, int? models)
    {
        if (sizes.Count == 0)
        {
            return null;
        }
        if (models is { } m && sizes.FirstOrDefault(s => s.Models == m) is { } exact)
        {
            return exact;
        }
        return sizes.OrderBy(s => s.Models).First();
    }

    // Faction detachments (+ enhancements) from the cached jsonb.
    private sealed record EnhancementInfo(string Id, string Name, int Points);
    private sealed record DetachmentInfo(string Id, string Name, int Dp, IReadOnlyList<EnhancementInfo> Enhancements);

    private static async Task<List<DetachmentInfo>> LoadDetachments(
        BattleLibraryDbContext db, string gameSystemKey, string factionId, CancellationToken ct)
    {
        var json = await db.CachedFactions
            .Where(f => f.BsDataId == factionId && f.GameSystemKey == gameSystemKey)
            .Where(f => db.CachedGameSystems.Any(s => s.Key == gameSystemKey && s.Enabled))
            .Select(f => f.DetachmentsJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        return JsonSerializer.Deserialize<List<DetachmentInfo>>(json, SlimJson) ?? [];
    }

    private static bool HasKeyword(ListEntry e, string keyword) =>
        e.Keywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase));

    // Content-tier checks, so the game screen can tell at a glance whether a list
    // leans on Legends (a "[Legends]" name suffix) or Crucible content.
    private static bool EntryIsLegends(ListEntry e) =>
        e.DatasheetName.Contains("[Legends]", StringComparison.OrdinalIgnoreCase);

    private static bool EntryIsCrucible(ListEntry e) =>
        e.DatasheetName.Contains("[Crucible]", StringComparison.OrdinalIgnoreCase)
        || e.Keywords.Any(k => string.Equals(k, "Crucible", StringComparison.OrdinalIgnoreCase));

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

    // Snapshots the chosen per-model loadout variants (name + points + count)
    // against the datasheet's variant groups; each count is clamped to [0, Max]
    // and a zero count is dropped. Unknown ids are ignored.
    private static List<SelectedOption> ResolveVariants(
        IReadOnlyList<ModelVariantGroup> groups, IReadOnlyList<VariantQuantityDto>? variants)
    {
        if (variants is null || variants.Count == 0)
        {
            return [];
        }
        var byId = groups.SelectMany(g => g.Variants).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var result = new List<SelectedOption>();
        foreach (var v in variants)
        {
            if (!byId.TryGetValue(v.Id, out var variant))
            {
                continue;
            }
            var qty = Math.Clamp(v.Quantity, 0, variant.Max);
            if (qty > 0)
            {
                result.Add(new SelectedOption { Id = variant.Id, Name = variant.Name, Points = variant.Points, Quantity = qty });
            }
        }
        return result;
    }

    // The entry's plain upgrade options (everything that isn't a loadout variant),
    // so a variant re-resolve leaves the wargear checkboxes untouched and vice
    // versa.
    private static List<SelectedOption> NonVariantOptions(ListEntry entry, IReadOnlyList<ModelVariantGroup> groups)
    {
        var variantIds = groups.SelectMany(g => g.Variants).Select(v => v.Id).ToHashSet();
        return entry.SelectedOptions.Where(o => !variantIds.Contains(o.Id)).ToList();
    }

    private static List<SelectedOption> VariantOptions(ListEntry entry, IReadOnlyList<ModelVariantGroup> groups)
    {
        var variantIds = groups.SelectMany(g => g.Variants).Select(v => v.Id).ToHashSet();
        return entry.SelectedOptions.Where(o => variantIds.Contains(o.Id)).ToList();
    }

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
