using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): proves a selection group's defaultSelectionEntryId
// is carried through to the parsed model - flagging the default wargear option
// and the default loadout variant - so the builder can pre-select it (H3).
public class BsDataDefaultSelectionTests
{
    private const string GameSystem = """
    {
      "gameSystem": {
        "id": "sys1",
        "name": "Test System",
        "revision": 1,
        "costTypes": [{ "id": "pts-id", "name": "pts" }],
        "profileTypes": [
          { "id": "pt-unit", "name": "Unit" },
          { "id": "pt-weapon", "name": "Melee Weapons" }
        ],
        "forceEntries": [{ "id": "force1", "name": "Army Roster" }]
      }
    }
    """;

    // A unit with a "pick one" weapon group whose default is the Choppa, and a
    // model loadout pool whose default variant is the Shoota Boy.
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntries": [
          {
            "id": "unit1", "name": "Test Mob", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              {
                "id": "p-stat", "name": "Test Mob", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "5" }]
              }
            ],
            "selectionEntryGroups": [
              {
                "id": "grp-melee", "name": "Melee Weapons", "defaultSelectionEntryId": "opt-choppa",
                "constraints": [
                  { "type": "min", "field": "selections", "scope": "parent", "value": 1 },
                  { "type": "max", "field": "selections", "scope": "parent", "value": 1 }
                ],
                "selectionEntries": [
                  { "id": "opt-choppa", "name": "Choppa", "type": "upgrade" },
                  {
                    "id": "opt-bigchoppa", "name": "Big Choppa", "type": "upgrade",
                    "costs": [{ "name": "pts", "typeId": "pts-id", "value": 5 }]
                  }
                ]
              },
              {
                "id": "grp-models", "name": "Boyz", "defaultSelectionEntryId": "m-shoota",
                "selectionEntries": [
                  {
                    "id": "m-slugga", "name": "Boy w/ Slugga", "type": "model",
                    "constraints": [{ "type": "max", "field": "selections", "scope": "parent", "value": 10 }]
                  },
                  {
                    "id": "m-shoota", "name": "Boy w/ Shoota", "type": "model",
                    "constraints": [{ "type": "max", "field": "selections", "scope": "parent", "value": 10 }]
                  }
                ]
              }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Flags_the_groups_default_wargear_option()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var unit = Assert.Single(model.Datasheets);
        var choppa = Assert.Single(unit.Options, o => o.Name == "Choppa");
        var bigChoppa = Assert.Single(unit.Options, o => o.Name == "Big Choppa");

        Assert.True(choppa.IsDefault, "The defaultSelectionEntryId option should be marked default.");
        Assert.False(bigChoppa.IsDefault, "A non-default option should not be marked default.");
    }

    [Fact]
    public void Flags_the_groups_default_loadout_variant()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var unit = Assert.Single(model.Datasheets);
        var pool = Assert.Single(unit.VariantGroups);
        var slugga = Assert.Single(pool.Variants, v => v.Name == "Boy w/ Slugga");
        var shoota = Assert.Single(pool.Variants, v => v.Name == "Boy w/ Shoota");

        Assert.True(shoota.IsDefault, "The defaultSelectionEntryId variant should be marked default.");
        Assert.False(slugga.IsDefault, "A non-default variant should not be marked default.");
    }

    [Fact]
    public void Leaves_options_undefaulted_when_group_declares_no_default()
    {
        // Same catalogue with the weapon group's default removed: no option is flagged.
        var noDefault = Catalogue.Replace("\"defaultSelectionEntryId\": \"opt-choppa\",", "");
        var model = new BsDataParser().ParseCatalogue(GameSystem, noDefault);

        var unit = Assert.Single(model.Datasheets);
        Assert.All(
            unit.Options.Where(o => o.Group == "Melee Weapons"),
            o => Assert.False(o.IsDefault));
    }
}
