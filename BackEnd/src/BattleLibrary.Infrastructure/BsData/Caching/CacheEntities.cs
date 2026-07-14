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

    // Faction-wide equipment names (jsonb array); empty for 40k.
    public string EquipmentJson { get; set; } = "[]";

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

    // Serialized Domain.Catalogue.Datasheet (jsonb).
    public string ModelJson { get; set; } = "";
}
