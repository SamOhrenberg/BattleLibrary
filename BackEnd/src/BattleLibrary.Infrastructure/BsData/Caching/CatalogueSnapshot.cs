using BattleLibrary.Domain.Catalogue;

namespace BattleLibrary.Infrastructure.BsData.Caching;

// One faction of a loaded system. Datasheets are in upstream display order
// (sortIndex, then name).
public sealed record CatalogueFaction(
    string Id,
    string Name,
    int Revision,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<Detachment> Detachments,
    IReadOnlyList<Datasheet> Datasheets);

// One game system's parsed catalogue, held in memory only and keyed to the
// source commit. Built once per refresh and never mutated, so readers share it
// without locking.
public sealed class CatalogueSnapshot
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Repo { get; init; }
    public required string CommitSha { get; init; }
    public required DateTimeOffset RefreshedAt { get; init; }
    public required GameSystemInfo System { get; init; }

    // Ordered by name.
    public required IReadOnlyList<CatalogueFaction> Factions { get; init; }
    public required IReadOnlyDictionary<string, CatalogueFaction> FactionsById { get; init; }

    // Factions share library units, so a datasheet id can appear in several
    // factions; this maps it to the first.
    public required IReadOnlyDictionary<string, Datasheet> DatasheetsById { get; init; }

    public int DatasheetCount => Factions.Sum(f => f.Datasheets.Count);

    // Point-less (e.g. Kill Team 2024) when no datasheet carries points.
    public bool Pointless { get; init; }

    public static CatalogueSnapshot Build(
        BsDataSource source, string commitSha, IReadOnlyList<CatalogueModel> models, DateTimeOffset refreshedAt)
    {
        // Empty factions (e.g. shared-library catalogues with no units) are skipped.
        var factions = models
            .Where(m => m.Datasheets.Count > 0)
            .Select(m => new CatalogueFaction(
                m.FactionId,
                m.FactionName,
                m.FactionRevision,
                m.Equipment,
                m.Detachments,
                m.Datasheets
                    .OrderBy(d => d.SortIndex)
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var factionsById = new Dictionary<string, CatalogueFaction>();
        var datasheetsById = new Dictionary<string, Datasheet>();
        foreach (var faction in factions)
        {
            factionsById.TryAdd(faction.Id, faction);
            foreach (var sheet in faction.Datasheets)
            {
                datasheetsById.TryAdd(sheet.Id, sheet);
            }
        }

        return new CatalogueSnapshot
        {
            Key = source.SystemKey,
            Name = source.DisplayName,
            Repo = source.Repo,
            CommitSha = commitSha,
            RefreshedAt = refreshedAt,
            // Roster-header identity is the same across every catalogue of a system.
            System = models.FirstOrDefault()?.GameSystem ?? new GameSystemInfo("", "", 0),
            Factions = factions,
            FactionsById = factionsById,
            DatasheetsById = datasheetsById,
            Pointless = !datasheetsById.Values.Any(d => d.Points != null),
        };
    }
}
