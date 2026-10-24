using System.Text.Json;
using BattleLibrary.Infrastructure.BsData;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Catalogue;

public static class CatalogueEndpoints
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record EnhancementDto(string Id, string Name, int Points);
    private sealed record DetachmentDto(string Id, string Name, int Dp, IReadOnlyList<EnhancementDto> Enhancements);

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
            .Select(f => new { f.Id, f.Name, f.BsDataId, f.EquipmentJson, f.DetachmentsJson })
            .FirstOrDefaultAsync(ct);
        if (faction is null)
        {
            return Results.NotFound();
        }

        var rows = await db.CachedDatasheets
            .Where(d => d.FactionId == faction.Id)
            .OrderBy(d => d.Name)
            .Select(d => new { id = d.BsDataId, name = d.Name, points = d.Points, keywords = d.KeywordsCsv })
            .ToListAsync(ct);
        var datasheets = rows.Select(d => new
        {
            d.id,
            d.name,
            d.points,
            keywords = string.IsNullOrEmpty(d.keywords) ? [] : d.keywords.Split('|'),
        });

        var equipment = DeserializeNames(faction.EquipmentJson);
        var detachments = DeserializeDetachments(faction.DetachmentsJson);

        return Results.Ok(new
        {
            faction = new { id = faction.BsDataId, name = faction.Name, equipment, detachments },
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
