using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): proves the parser honors the BSData "collective"
// flag when deciding which model groups are distributable shared pools, rather
// than inferring a pool purely from a group holding more than one model variant.
public class BsDataCollectiveTests
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

    // One unit with three model groups:
    //  - "Special Weapon": collective, a single model variant  -> a pool (the L2 fix)
    //  - "Sergeant": not collective, a single fixed model      -> a fixed model
    //  - "Heavy Weapon": collective, two model variants        -> a pool (unchanged)
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntries": [
          {
            "id": "unit1", "name": "Vanguard Squad", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 100 }],
            "profiles": [
              {
                "id": "p-stat", "name": "Vanguard Squad", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "2" }]
              }
            ],
            "selectionEntryGroups": [
              {
                "id": "grp-special", "name": "Special Weapon", "collective": true,
                "selectionEntries": [
                  {
                    "id": "m-plasma", "name": "Plasma Trooper", "type": "model",
                    "constraints": [
                      { "type": "max", "field": "selections", "scope": "parent", "value": 2 }
                    ]
                  }
                ]
              },
              {
                "id": "grp-sergeant", "name": "Sergeant",
                "selectionEntries": [
                  { "id": "m-sergeant", "name": "Sergeant", "type": "model" }
                ]
              },
              {
                "id": "grp-heavy", "name": "Heavy Weapon", "collective": true,
                "selectionEntries": [
                  { "id": "m-lascannon", "name": "Lascannon Trooper", "type": "model" },
                  { "id": "m-missile", "name": "Missile Trooper", "type": "model" }
                ]
              }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Collective_single_variant_group_is_a_distributable_pool()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);
        var sheet = Assert.Single(model.Datasheets);

        // The collective flag makes a single-variant group a pool. Before honoring
        // the flag this group was miscounted as a fixed model instead.
        var special = Assert.Single(sheet.VariantGroups, g => g.Name == "Special Weapon");
        var variant = Assert.Single(special.Variants);
        Assert.Equal("Plasma Trooper", variant.Name);
        Assert.Equal(2, special.Max);
    }

    [Fact]
    public void Non_collective_single_model_group_stays_a_fixed_model()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);
        var sheet = Assert.Single(model.Datasheets);

        Assert.DoesNotContain(sheet.VariantGroups, g => g.Name == "Sergeant");
        Assert.Equal(1, sheet.FixedModelCount); // the lone Sergeant
    }

    [Fact]
    public void Collective_multi_variant_group_remains_a_pool()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);
        var sheet = Assert.Single(model.Datasheets);

        var heavy = Assert.Single(sheet.VariantGroups, g => g.Name == "Heavy Weapon");
        Assert.Equal(2, heavy.Variants.Count);
    }
}
