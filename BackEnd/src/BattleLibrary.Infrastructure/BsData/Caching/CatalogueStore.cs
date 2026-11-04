using System.Collections.Immutable;

namespace BattleLibrary.Infrastructure.BsData.Caching;

// Process-memory home of every loaded BSData catalogue. Nothing here is ever
// written to disk; a restart reloads from upstream. Disabled systems are absent.
public sealed class CatalogueStore
{
    private ImmutableDictionary<string, CatalogueSnapshot> snapshots =
        ImmutableDictionary<string, CatalogueSnapshot>.Empty;

    private volatile bool ready;

    // Serializes rebuilds so the background poller and admin calls never interleave.
    public SemaphoreSlim RefreshLock { get; } = new(1, 1);

    // False until the startup load finishes, so readers can answer 503, not 404.
    public bool Ready => ready;

    public void MarkReady() => ready = true;

    public IEnumerable<CatalogueSnapshot> All => snapshots.Values;

    public CatalogueSnapshot? Get(string systemKey) => snapshots.GetValueOrDefault(systemKey);

    public void Set(CatalogueSnapshot snapshot) => ImmutableInterlocked.Update(ref snapshots, s => s.SetItem(snapshot.Key, snapshot));

    public void Remove(string systemKey) => ImmutableInterlocked.TryRemove(ref snapshots, systemKey, out _);

    public CatalogueFaction? FindFaction(string systemKey, string factionId) =>
        Get(systemKey)?.FactionsById.GetValueOrDefault(factionId);

    // Faction ids are BSData catalogue ids, unique across the configured repos.
    public (CatalogueSnapshot System, CatalogueFaction Faction)? FindFaction(string factionId)
    {
        foreach (var system in snapshots.Values)
        {
            if (system.FactionsById.TryGetValue(factionId, out var faction))
            {
                return (system, faction);
            }
        }
        return null;
    }
}
