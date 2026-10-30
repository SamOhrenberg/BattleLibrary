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
        group.MapPost("/{id:guid}/duplicate", DuplicateList);
        group.MapDelete("/{id:guid}", DeleteList);
        group.MapPost("/{id:guid}/featured", SetFeatured);
        group.MapPost("/{id:guid}/battle-size", SetBattleSize);
        group.MapPost("/{id:guid}/detachments", SetDetachments);
        group.MapPost("/{id:guid}/entries", AddEntry);
        group.MapPut("/{id:guid}/entries/{entryId:guid}", UpdateEntry);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/size", SetModelCount);
        group.MapPost("/{id:guid}/entries/{entryId:guid}/warlord", SetWarlord);
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
        var copy = new ArmyList
        {
            UserId = userId,
            Name = $"{baseName} (copy)",
            GameSystemKey = source.GameSystemKey,
            FactionId = source.FactionId,
            FactionName = source.FactionName,
            PointsLimit = source.PointsLimit,
            BattleSize = source.BattleSize,
            Featured = false,
            Detachments = source.Detachments
                .Select(d => new ListDetachment { DetachmentId = d.DetachmentId, Name = d.Name, Dp = d.Dp })
                .ToList(),
            Entries = source.Entries.Select(e => new ListEntry
            {
                DatasheetId = e.DatasheetId,
                DatasheetName = e.DatasheetName,
                Points = e.Points,
                ModelCount = e.ModelCount,
                Quantity = e.Quantity,
                Keywords = e.Keywords.ToList(),
                IsWarlord = e.IsWarlord,
                EnhancementId = e.EnhancementId,
                EnhancementName = e.EnhancementName,
                EnhancementPoints = e.EnhancementPoints,
                SelectedOptions = e.SelectedOptions
                    .Select(o => new SelectedOption { Id = o.Id, Name = o.Name, Points = o.Points })
                    .ToList(),
            }).ToList(),
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

        // One entry per unit; multiple units of a datasheet are separate entries.
        var size = PickSize(sheet.Sizes, request.Models);
        var entry = new ListEntry
        {
            ArmyListId = list.Id,
            DatasheetId = request.DatasheetId,
            DatasheetName = sheet.Name,
            Points = size?.Points ?? sheet.Points,
            ModelCount = size?.Models ?? 1,
            Keywords = sheet.Keywords.ToList(),
            ForcedWarlord = sheet.ForcedWarlord,
            SelectedOptions = ResolveOptions(sheet.Options, request.SelectedOptionIds),
        };
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
        if (request.SelectedOptionIds is not null)
        {
            var sheet = await LoadSheet(db, entry.DatasheetId, list.GameSystemKey, ct);
            entry.SelectedOptions = ResolveOptions(sheet?.Options ?? [], request.SelectedOptionIds);
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
                    e.Id, e.DatasheetId, e.DatasheetName, e.Points, optionsPoints, e.ModelCount,
                    e.Points + optionsPoints + e.EnhancementPoints,
                    e.Keywords, e.IsWarlord, e.ForcedWarlord, e.EnhancementId, e.EnhancementName, e.EnhancementPoints,
                    e.SelectedOptions.Select(o => new SelectedOptionDto(o.Id, o.Name, o.Points)).ToList());
            })
            .ToList();
        var total = entries.Sum(e => e.LineTotal);
        var overLimit = list.PointsLimit.HasValue && total > list.PointsLimit.Value;
        var detachments = list.Detachments
            .Select(d => new ListDetachmentDto(d.DetachmentId, d.Name, d.Dp))
            .ToList();
        return new ArmyListDto(
            list.Id, list.Name, list.GameSystemKey, list.FactionId, list.FactionName,
            list.PointsLimit, total, overLimit, list.Featured,
            list.BattleSize, list.Entries.Any(EntryIsLegends), list.Entries.Any(EntryIsCrucible),
            detachments, list.UpdatedAt, entries);
    }

    private static int LineTotal(ListEntry e) =>
        e.Points + e.SelectedOptions.Sum(o => o.Points) + e.EnhancementPoints;

    private sealed record SheetInfo(
        string Name, int Points, IReadOnlyList<string> Keywords, IReadOnlyList<DatasheetOption> Options, IReadOnlyList<UnitSize> Sizes,
        bool ForcedWarlord);

    // Pulls name/points/keywords/options/sizes for a datasheet from the cached slim jsonb.
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
            : new SheetInfo(sheet.Name, sheet.Points ?? 0, sheet.Keywords, sheet.Options, sheet.Sizes ?? [], IsForcedWarlord(sheet));
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

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
