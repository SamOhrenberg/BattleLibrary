using System.Text.Json;
using BattleLibrary.Api.Hosting;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData;
using BattleLibrary.Infrastructure.BsData.Caching;

namespace BattleLibrary.Api.Catalogue;

public static class CatalogueEndpoints
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // Max results returned from a rule search, so a short query can't flood.
    private const int SearchLimit = 60;

    private sealed record AbilityDto(string Name, string Text);
    private sealed record AvailabilityDto(IReadOnlyList<string> RequiredDetachmentIds, IReadOnlyList<string> RequiredToggles);
    private sealed record EnhancementDto(string Id, string Name, int Points, string Text);
    private sealed record DetachmentDto(
        string Id, string Name, int Dp, IReadOnlyList<EnhancementDto> Enhancements, IReadOnlyList<AbilityDto> Rules,
        IReadOnlyList<string> DispositionIds);
    private sealed record DispositionDto(string Id, string Name);

    private static DetachmentDto ToDto(Detachment d) => new(
        d.Id, d.Name, d.Dp,
        d.Enhancements.Select(e => new EnhancementDto(e.Id, e.Name, e.Points, e.Text)).ToList(),
        d.Rules.Select(r => new AbilityDto(r.Name, r.Text)).ToList(),
        d.DispositionIds);

    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder routes)
    {
        var read = routes.MapGroup("/api/catalogue").AddEndpointFilter(RequireLoaded);
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

    // The catalogue is loaded into memory after startup; until then, 503.
    private static async ValueTask<object?> RequireLoaded(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<CatalogueStore>().Ready)
        {
            context.HttpContext.Response.Headers.RetryAfter = "15";
            return Results.Problem("The catalogue is still loading.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        return await next(context);
    }

    private static IResult GetSystems(CatalogueStore store)
    {
        var systems = store.All
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Key,
                s.Name,
                s.CommitSha,
                s.RefreshedAt,
                Factions = s.Factions.Count,
                Datasheets = s.DatasheetCount,
                s.Pointless,
            });
        return Results.Ok(systems);
    }

    // Faction/datasheet ids exposed to clients are the stable BSData ids.
    private static IResult GetFactions(string systemKey, CatalogueStore store)
    {
        if (store.Get(systemKey) is not { } system)
        {
            return Results.NotFound();
        }
        var factions = system.Factions
            .Select(f => new { id = f.Id, name = f.Name, datasheets = f.Datasheets.Count });
        return Results.Ok(factions);
    }

    private static IResult GetDatasheets(string factionId, CatalogueStore store)
    {
        if (store.FindFaction(factionId) is not var (system, faction))
        {
            return Results.NotFound();
        }

        var datasheets = faction.Datasheets.Select(d => new
        {
            id = d.Id,
            name = d.Name,
            points = d.Points,
            keywords = d.Keywords,
            availability = new AvailabilityDto(d.Availability.RequiredDetachmentIds, d.Availability.RequiredToggles),
        });
        var detachments = faction.Detachments.Select(ToDto);
        var dispositions = system.System.Dispositions.Select(x => new DispositionDto(x.Id, x.Name));

        return Results.Ok(new
        {
            faction = new { id = faction.Id, name = faction.Name, equipment = faction.Equipment, detachments, dispositions },
            datasheets,
        });
    }

    private static IResult GetDatasheet(string datasheetId, CatalogueStore store)
    {
        var sheet = store.All
            .Select(s => s.DatasheetsById.GetValueOrDefault(datasheetId))
            .FirstOrDefault(d => d is not null);
        return sheet is null ? Results.NotFound() : Results.Json(sheet, WebJson);
    }

    private sealed record SearchResultDto(
        string Kind, string Title, string? Subtitle, string? Text,
        string? DatasheetId, string? FactionId, string? FactionName);

    // Rule Search over the loaded catalogue: datasheet names, unit ability names
    // + text, weapon names, and detachment rules + enhancements. No rules text
    // beyond what's already loaded.
    private static IResult Search(string systemKey, string? q, CatalogueStore store)
    {
        var term = q?.Trim() ?? "";
        if (term.Length < 2)
        {
            return Results.Ok(Array.Empty<SearchResultDto>());
        }
        if (store.Get(systemKey) is not { } system)
        {
            return Results.NotFound();
        }

        var contains = (string? s) => !string.IsNullOrEmpty(s) && s.Contains(term, StringComparison.OrdinalIgnoreCase);
        var results = new List<SearchResultDto>();

        foreach (var faction in system.Factions)
        {
            foreach (var sheet in faction.Datasheets)
            {
                if (contains(sheet.Name))
                {
                    results.Add(new SearchResultDto("unit", sheet.Name, faction.Name, null, sheet.Id, faction.Id, faction.Name));
                }
                foreach (var a in sheet.Abilities.Concat(sheet.Rules).Where(a => contains(a.Name) || contains(a.Text)))
                {
                    results.Add(new SearchResultDto("ability", a.Name, sheet.Name, a.Text, sheet.Id, faction.Id, faction.Name));
                }
                foreach (var w in sheet.Weapons.Where(w => contains(w.Name)).DistinctBy(w => w.Name))
                {
                    results.Add(new SearchResultDto("weapon", w.Name, sheet.Name, null, sheet.Id, faction.Id, faction.Name));
                }
            }

            foreach (var det in faction.Detachments)
            {
                foreach (var rule in det.Rules.Where(r => contains(r.Name) || contains(r.Text)))
                {
                    results.Add(new SearchResultDto("detachment-rule", rule.Name, det.Name, rule.Text, null, faction.Id, faction.Name));
                }
                foreach (var enh in det.Enhancements.Where(e => contains(e.Name) || contains(e.Text)))
                {
                    results.Add(new SearchResultDto("enhancement", enh.Name, det.Name, enh.Text, null, faction.Id, faction.Name));
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

    private static bool IsKnownSystem(string key) => BsDataSource.All.Any(s => s.SystemKey == key);

    // Refresh one system (?system=key) or all configured systems.
    private static async Task<IResult> Refresh(BsDataRefreshService service, bool? force, string? system, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(system))
        {
            if (!IsKnownSystem(system))
            {
                return Results.NotFound();
            }
            return Results.Ok(new[] { await service.RefreshAsync(system, force ?? false, ct) });
        }
        return Results.Ok(await service.RefreshAllAsync(force ?? false, ct));
    }

    private static async Task<IResult> Purge(string key, BsDataRefreshService service, CancellationToken ct)
    {
        if (!IsKnownSystem(key))
        {
            return Results.NotFound();
        }
        await service.PurgeAsync(key, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetEnabled(string key, bool value, BsDataRefreshService service, CancellationToken ct)
    {
        if (!IsKnownSystem(key))
        {
            return Results.NotFound();
        }
        await service.SetEnabledAsync(key, value, ct);
        return Results.NoContent();
    }
}
