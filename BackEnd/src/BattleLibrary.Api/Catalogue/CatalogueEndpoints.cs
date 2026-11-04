using System.Text.Json;
using BattleLibrary.Api.Hosting;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Catalogue;

public static class CatalogueEndpoints
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // Max results returned from a rule search, so a short query can't flood.
    private const int SearchLimit = 60;

    private sealed record AbilityDto(string Name, string Text);
    private sealed record AvailabilityDto(IReadOnlyList<string> RequiredDetachmentIds, IReadOnlyList<string> RequiredToggles);

    private static AvailabilityDto DeserializeAvailability(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AvailabilityDto([], []);
        }
        try
        {
            return JsonSerializer.Deserialize<AvailabilityDto>(json, WebJson) ?? new AvailabilityDto([], []);
        }
        catch (JsonException)
        {
            return new AvailabilityDto([], []);
        }
    }
    private sealed record EnhancementDto(string Id, string Name, int Points, string Text);
    private sealed record DetachmentDto(
        string Id, string Name, int Dp, IReadOnlyList<EnhancementDto> Enhancements, IReadOnlyList<AbilityDto> Rules,
        IReadOnlyList<string> DispositionIds);
    private sealed record DispositionDto(string Id, string Name);

    private static IReadOnlyList<DispositionDto> DeserializeDispositions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<DispositionDto>>(json, WebJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] DeserializeNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<DetachmentDto> DeserializeDetachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<DetachmentDto>>(json, WebJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder routes)
    {
        var read = routes.MapGroup("/api/catalogue");
        read.MapGet("/systems", GetSystems);
        read.MapGet("/{systemKey}/factions", GetFactions);
        read.MapGet("/factions/{factionId}/datasheets", GetDatasheets);
        read.MapGet("/datasheets/{datasheetId}", GetDatasheet);
        read.MapGet("/{systemKey}/search", Search).RequireRateLimiting(HostingExtensions.SearchPolicy);

        var admin = routes.MapGroup("/api/admin/catalogue").AddEndpointFilter<AdminKeyFilter>();
        admin.MapPost("/refresh", Refresh);
        admin.MapPost("/{key}/purge", Purge);
        admin.MapPost("/{key}/enabled", SetEnabled);
    }

    private static async Task<IResult> GetSystems(BattleLibraryDbContext db, CancellationToken ct)
    {
        var systems = await db.CachedGameSystems
            .Where(s => s.Enabled)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Key,
                s.Name,
                s.CommitSha,
                s.RefreshedAt,
                Factions = s.Factions.Count,
                Datasheets = s.Factions.Sum(f => f.DatasheetCount),
                // A system is point-less (e.g. Kill Team 2024) when no datasheet carries points.
                Pointless = !s.Factions.Any(f => f.Datasheets.Any(d => d.Points != null)),
            })
            .ToListAsync(ct);
        return Results.Ok(systems);
    }

    // Faction/datasheet ids exposed to clients are the stable BSData ids, not the
    // internal rows (which are replaced on every refresh).
    private static async Task<IResult> GetFactions(string systemKey, BattleLibraryDbContext db, CancellationToken ct)
    {
        var enabled = await db.CachedGameSystems.AnyAsync(s => s.Key == systemKey && s.Enabled, ct);
        if (!enabled)
        {
            return Results.NotFound();
        }

        var factions = await db.CachedFactions
            .Where(f => f.GameSystemKey == systemKey)
            .OrderBy(f => f.Name)
            .Select(f => new { id = f.BsDataId, name = f.Name, datasheets = f.DatasheetCount })
            .ToListAsync(ct);
        return Results.Ok(factions);
    }

    private static async Task<IResult> GetDatasheets(string factionId, BattleLibraryDbContext db, CancellationToken ct)
    {
        var faction = await db.CachedFactions
            .Where(f => f.BsDataId == factionId)
            .Where(f => db.CachedGameSystems.Any(s => s.Key == f.GameSystemKey && s.Enabled))
            .Select(f => new { f.Id, f.Name, f.BsDataId, f.EquipmentJson, f.DetachmentsJson, Dispositions = f.GameSystem.DispositionsJson })
            .FirstOrDefaultAsync(ct);
        if (faction is null)
        {
            return Results.NotFound();
        }

        var rows = await db.CachedDatasheets
            .Where(d => d.FactionId == faction.Id)
            .OrderBy(d => d.SortIndex)
            .ThenBy(d => d.Name)
            .Select(d => new { id = d.BsDataId, name = d.Name, points = d.Points, keywords = d.KeywordsCsv, availability = d.AvailabilityJson })
            .ToListAsync(ct);
        var datasheets = rows.Select(d => new
        {
            d.id,
            d.name,
            d.points,
            keywords = string.IsNullOrEmpty(d.keywords) ? [] : d.keywords.Split('|'),
            availability = DeserializeAvailability(d.availability),
        });

        var equipment = DeserializeNames(faction.EquipmentJson);
        var detachments = DeserializeDetachments(faction.DetachmentsJson);
        var dispositions = DeserializeDispositions(faction.Dispositions);

        return Results.Ok(new
        {
            faction = new { id = faction.BsDataId, name = faction.Name, equipment, detachments, dispositions },
            datasheets,
        });
    }

    private static async Task<IResult> GetDatasheet(string datasheetId, BattleLibraryDbContext db, CancellationToken ct)
    {
        var json = await db.CachedDatasheets
            .Where(d => d.BsDataId == datasheetId)
            .Where(d => db.CachedGameSystems.Any(s => s.Key == d.Faction.GameSystemKey && s.Enabled))
            .Select(d => d.ModelJson)
            .FirstOrDefaultAsync(ct);

        return json is null ? Results.NotFound() : Results.Content(json, "application/json");
    }

    // Rows pulled by the search prefilter (the text match runs in Postgres).
    private sealed record DatasheetSearchRow(string DatasheetId, string DatasheetName, string FactionId, string FactionName, string ModelJson);
    private sealed record FactionSearchRow(string FactionId, string FactionName, string DetachmentsJson);

    private sealed record SearchResultDto(
        string Kind, string Title, string? Subtitle, string? Text,
        string? DatasheetId, string? FactionId, string? FactionName);

    // Rule Search over already-cached content: datasheet names, unit ability
    // names + text, weapon names, and detachment rules + enhancements. Postgres
    // does the text match on the jsonb (cast to text); matches are then classified
    // in memory. No new ingest, no rules text beyond what's already cached.
    private static async Task<IResult> Search(string systemKey, string? q, BattleLibraryDbContext db, CancellationToken ct)
    {
        var term = q?.Trim() ?? "";
        if (term.Length < 2)
        {
            return Results.Ok(Array.Empty<SearchResultDto>());
        }
        var enabled = await db.CachedGameSystems.AnyAsync(s => s.Key == systemKey && s.Enabled, ct);
        if (!enabled)
        {
            return Results.NotFound();
        }

        // Escape ILIKE wildcards so the query is treated literally.
        var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var contains = (string? s) => !string.IsNullOrEmpty(s) && s.Contains(term, StringComparison.OrdinalIgnoreCase);

        var results = new List<SearchResultDto>();

        var sheetRows = await db.Database.SqlQuery<DatasheetSearchRow>($"""
            SELECT d."BsDataId" AS "DatasheetId", d."Name" AS "DatasheetName",
                   f."BsDataId" AS "FactionId", f."Name" AS "FactionName", d."ModelJson" AS "ModelJson"
            FROM "CachedDatasheets" d
            JOIN "CachedFactions" f ON f."Id" = d."FactionId"
            JOIN "CachedGameSystems" s ON s."Key" = f."GameSystemKey"
            WHERE s."Key" = {systemKey} AND s."Enabled" AND d."ModelJson"::text ILIKE {pattern}
            """).ToListAsync(ct);

        foreach (var row in sheetRows)
        {
            Datasheet? sheet;
            try
            {
                sheet = JsonSerializer.Deserialize<Datasheet>(row.ModelJson, WebJson);
            }
            catch (JsonException)
            {
                continue;
            }
            if (sheet is null)
            {
                continue;
            }

            if (contains(sheet.Name))
            {
                results.Add(new SearchResultDto("unit", sheet.Name, row.FactionName, null, row.DatasheetId, row.FactionId, row.FactionName));
            }
            foreach (var a in sheet.Abilities.Concat(sheet.Rules).Where(a => contains(a.Name) || contains(a.Text)))
            {
                results.Add(new SearchResultDto("ability", a.Name, sheet.Name, a.Text, row.DatasheetId, row.FactionId, row.FactionName));
            }
            foreach (var w in sheet.Weapons.Where(w => contains(w.Name)).DistinctBy(w => w.Name))
            {
                results.Add(new SearchResultDto("weapon", w.Name, sheet.Name, null, row.DatasheetId, row.FactionId, row.FactionName));
            }
        }

        var factionRows = await db.Database.SqlQuery<FactionSearchRow>($"""
            SELECT f."BsDataId" AS "FactionId", f."Name" AS "FactionName", f."DetachmentsJson" AS "DetachmentsJson"
            FROM "CachedFactions" f
            JOIN "CachedGameSystems" s ON s."Key" = f."GameSystemKey"
            WHERE s."Key" = {systemKey} AND s."Enabled" AND f."DetachmentsJson"::text ILIKE {pattern}
            """).ToListAsync(ct);

        foreach (var row in factionRows)
        {
            foreach (var det in DeserializeDetachments(row.DetachmentsJson))
            {
                foreach (var rule in det.Rules.Where(r => contains(r.Name) || contains(r.Text)))
                {
                    results.Add(new SearchResultDto("detachment-rule", rule.Name, det.Name, rule.Text, null, row.FactionId, row.FactionName));
                }
                foreach (var enh in det.Enhancements.Where(e => contains(e.Name) || contains(e.Text)))
                {
                    results.Add(new SearchResultDto("enhancement", enh.Name, det.Name, enh.Text, null, row.FactionId, row.FactionName));
                }
            }
        }

        // Exact-ish name matches first, then by kind, capped.
        var ordered = results
            .OrderByDescending(r => string.Equals(r.Title, term, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Title)
            .Take(SearchLimit)
            .ToList();
        return Results.Ok(ordered);
    }

    // Refresh one system (?system=key) or all configured systems.
    private static async Task<IResult> Refresh(BsDataRefreshService service, bool? force, string? system, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(system))
        {
            return Results.Ok(new[] { await service.RefreshAsync(system, force ?? false, ct) });
        }
        return Results.Ok(await service.RefreshAllAsync(force ?? false, ct));
    }

    private static async Task<IResult> Purge(string key, BsDataRefreshService service, CancellationToken ct)
    {
        await service.PurgeAsync(key, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetEnabled(string key, bool value, BsDataRefreshService service, CancellationToken ct)
    {
        await service.SetEnabledAsync(key, value, ct);
        return Results.NoContent();
    }
}
