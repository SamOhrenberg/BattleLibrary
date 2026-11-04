using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData;
using BattleLibrary.Infrastructure.BsData.Caching;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic parsed models fed into the in-memory
// snapshot, checking the lookups and ordering the read endpoints rely on.
public class CatalogueSnapshotTests
{
    private static Datasheet Sheet(string id, string name, int? points, long sortIndex = long.MaxValue) => new(
        id, name, points, [], "", [], [], [], [], [], [], [], [], 1, DatasheetAvailability.None, [], SortIndex: sortIndex);

    private static CatalogueModel Faction(string id, string name, params Datasheet[] sheets) =>
        new(new GameSystemInfo("sys1", "Test System", 3), id, name, 7, sheets, [], []);

    [Fact]
    public void BuildSkipsEmptyFactionsAndOrdersByName()
    {
        var snapshot = CatalogueSnapshot.Build(BsDataSource.Wh40k11e, "sha1",
        [
            Faction("f-z", "Zeta", Sheet("d1", "Unit", 10)),
            Faction("lib", "Library"),
            Faction("f-a", "Alpha", Sheet("d2", "Unit", 20)),
        ], DateTimeOffset.UtcNow);

        Assert.Equal(["Alpha", "Zeta"], snapshot.Factions.Select(f => f.Name));
        Assert.False(snapshot.FactionsById.ContainsKey("lib"));
        Assert.Equal("sys1", snapshot.System.Id);
        Assert.Equal("sha1", snapshot.CommitSha);
        Assert.Equal(2, snapshot.DatasheetCount);
    }

    [Fact]
    public void BuildOrdersDatasheetsBySortIndexThenName()
    {
        var snapshot = CatalogueSnapshot.Build(BsDataSource.Wh40k11e, "sha1",
        [
            Faction("f", "Faction",
                Sheet("c", "Charlie", 1),
                Sheet("b", "Bravo", 1, sortIndex: 2),
                Sheet("a", "Alpha", 1),
                Sheet("d", "Delta", 1, sortIndex: 1)),
        ], DateTimeOffset.UtcNow);

        Assert.Equal(["Delta", "Bravo", "Alpha", "Charlie"], snapshot.Factions[0].Datasheets.Select(d => d.Name));
    }

    [Fact]
    public void SharedDatasheetResolvesAndFactionLookupsWork()
    {
        var shared = Sheet("shared", "Rhino", 75);
        var snapshot = CatalogueSnapshot.Build(BsDataSource.Wh40k11e, "sha1",
        [
            Faction("f-a", "Alpha", shared),
            Faction("f-b", "Bravo", shared),
        ], DateTimeOffset.UtcNow);

        var store = new CatalogueStore();
        store.Set(snapshot);

        Assert.Same(shared, snapshot.DatasheetsById["shared"]);
        Assert.Equal("Bravo", store.FindFaction(BsDataSource.Wh40k11e.SystemKey, "f-b")?.Name);
        Assert.Equal(BsDataSource.Wh40k11e.SystemKey, store.FindFaction("f-a")?.System.Key);

        store.Remove(BsDataSource.Wh40k11e.SystemKey);
        Assert.Null(store.FindFaction("f-a"));
    }

    [Fact]
    public void PointlessWhenNoDatasheetCarriesPoints()
    {
        var pointless = CatalogueSnapshot.Build(BsDataSource.KillTeam2024, "sha1",
            [Faction("kt", "Team", Sheet("o1", "Operative", null))], DateTimeOffset.UtcNow);
        var pointed = CatalogueSnapshot.Build(BsDataSource.Wh40k11e, "sha1",
            [Faction("f", "Faction", Sheet("d1", "Unit", null), Sheet("d2", "Unit 2", 5))], DateTimeOffset.UtcNow);

        Assert.True(pointless.Pointless);
        Assert.False(pointed.Pointless);
    }
}
