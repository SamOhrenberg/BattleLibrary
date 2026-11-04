using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic catalogue proving that BSData's
// sortIndex drives the display order of datasheets and of a unit's option
// choices, instead of the old alphabetical-by-name ordering. Entries are laid
// out so sortIndex order differs from both alphabetical and document order.
public class BsDataSortIndexTests
{
    private const string GameSystem = """
    {
      "gameSystem": {
        "id": "sys1",
        "name": "Test System",
        "revision": 1,
        "costTypes": [{ "id": "pts-id", "name": "pts" }],
        "profileTypes": [{ "id": "pt-unit", "name": "Unit" }],
        "forceEntries": [{ "id": "force1", "name": "Army Roster" }]
      }
    }
    """;

    // Four units. sortIndex order is Mu (0), Zeta (1), Alpha (2), then Beta
    // (no sortIndex -> last); alphabetical would be Alpha, Beta, Mu, Zeta.
    // Mu carries a "Weapons" option group whose choices list Choppa before
    // Shoota but index Shoota (0) before Choppa (1).
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntries": [
          {
            "id": "u-mu", "name": "Mu Squad", "type": "unit", "sortIndex": 0,
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              { "id": "p-mu", "name": "Mu Squad", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "2" }] }
            ],
            "selectionEntryGroups": [
              {
                "id": "g-weapons", "name": "Weapons",
                "selectionEntries": [
                  { "id": "o-choppa", "name": "Choppa", "type": "upgrade", "sortIndex": 1 },
                  { "id": "o-shoota", "name": "Shoota", "type": "upgrade", "sortIndex": 0 }
                ]
              }
            ]
          },
          {
            "id": "u-zeta", "name": "Zeta Squad", "type": "unit", "sortIndex": 1,
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              { "id": "p-zeta", "name": "Zeta Squad", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "2" }] }
            ]
          },
          {
            "id": "u-alpha", "name": "Alpha Squad", "type": "unit", "sortIndex": 2,
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              { "id": "p-alpha", "name": "Alpha Squad", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "2" }] }
            ]
          },
          {
            "id": "u-beta", "name": "Beta Squad", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              { "id": "p-beta", "name": "Beta Squad", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "2" }] }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Datasheets_follow_sortIndex_then_name()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var order = model.Datasheets.Select(d => d.Name).ToList();
        Assert.Equal(new[] { "Mu Squad", "Zeta Squad", "Alpha Squad", "Beta Squad" }, order);
    }

    [Fact]
    public void Option_choices_follow_sortIndex()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var mu = model.Datasheets.Single(d => d.Name == "Mu Squad");
        var weapons = mu.Options.Where(o => o.Group == "Weapons").Select(o => o.Name).ToList();
        Assert.Equal(new[] { "Shoota", "Choppa" }, weapons);
    }
}
