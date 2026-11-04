namespace BattleLibrary.Infrastructure.BsData.Caching;

// Cached BSData read model. Never committed to git; refreshed from upstream and
// keyed to the source commit. Enabled is the per-system kill switch.
public sealed class CachedGameSystem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Repo { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset RefreshedAt { get; set; }

    // BattleScribe roster-header identity: the source gameSystem id/name/revision
    // and the points cost-type id, so exported .ros files match the right
    // catalogue and imports can resolve the system. Empty on point-less systems.
    public string BsDataId { get; set; } = "";
    public string BsDataName { get; set; } = "";
    public int Revision { get; set; }
    public string PointsCostTypeId { get; set; } = "";

    // The system's primary roster force slot (e.g. "Army Roster"); a roster's
    // <force> must reference it by id or importers reject it.
    public string ForceEntryId { get; set; } = "";
    public string ForceEntryName { get; set; } = "";

    // Army-wide Force Disposition options (jsonb array of {id,name}); empty for
    // systems without them (e.g. Kill Team).
    public string DispositionsJson { get; set; } = "[]";

    public List<CachedFaction> Factions { get; set; } = [];
}

public sealed class CachedFaction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string GameSystemKey { get; set; } = "";
    public CachedGameSystem GameSystem { get; set; } = null!;

    public string BsDataId { get; set; } = "";
    public string Name { get; set; } = "";
    public int DatasheetCount { get; set; }

    // Source catalogue revision, for the roster force's catalogueRevision.
    public int Revision { get; set; }

    // Faction-wide equipment names (jsonb array); empty for 40k.
    public string EquipmentJson { get; set; } = "[]";

    // Detachments + their enhancements (jsonb); empty for point-less systems.
    public string DetachmentsJson { get; set; } = "[]";

    public List<CachedDatasheet> Datasheets { get; set; } = [];
}

public sealed class CachedDatasheet
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FactionId { get; set; }
    public CachedFaction Faction { get; set; } = null!;

    public string BsDataId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Points { get; set; }

    // Upstream display order (BSData sortIndex), so the catalogue list can honor
    // it without deserializing the full model. long.MaxValue when the source
    // omits it, so unindexed units sort last, name breaking ties.
    public long SortIndex { get; set; } = long.MaxValue;

    // Keywords joined (for grouping the catalogue by unit type without
    // deserializing the full model); the full set lives in ModelJson.
    public string KeywordsCsv { get; set; } = "";

    // Serialized Domain.Catalogue.DatasheetAvailability (jsonb): the unit's
    // detachment / config-toggle gates, so the catalogue list can be filtered
    // without deserializing the full model.
    public string AvailabilityJson { get; set; } = "{}";

    // Serialized Domain.Catalogue.Datasheet (jsonb).
    public string ModelJson { get; set; } = "";
}
