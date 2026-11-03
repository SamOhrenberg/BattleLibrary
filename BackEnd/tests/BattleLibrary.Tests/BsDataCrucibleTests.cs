using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): a synthetic catalogue proving Crucible warband
// pricing. A Crucible character attaches its weapons by a group link and prices
// them with selection-counting modifiers: +5 per weapon and -10 at 2+ weapons.
// A plain (non-Crucible) unit linking the same group gains neither the options
// nor the rules, so ordinary datasheets stay untouched.
public class BsDataCrucibleTests
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

    // "grp-weapons" is a shared group linked into both units. Only the Crucible
    // one follows the link (and prices it). The pts modifiers live in the unit's
    // own modifierGroups, counting selections in the weapons group.
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntryGroups": [
          {
            "id": "grp-weapons", "name": "Weapons",
            "selectionEntries": [
              { "id": "w-choppa", "name": "Choppa", "type": "upgrade" },
              { "id": "w-shoota", "name": "Shoota", "type": "upgrade" },
              { "id": "w-klaw", "name": "Power Klaw", "type": "upgrade" }
            ]
          }
        ],
        "sharedSelectionEntries": [
          {
            "id": "u-cru", "name": "Test Warboss [Crucible]", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 75 }],
            "profiles": [
              {
                "id": "p-cru", "name": "Test Warboss", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "6" }]
              }
            ],
            "entryLinks": [
              { "id": "el-w", "name": "Weapons", "type": "selectionEntryGroup", "targetId": "grp-weapons" }
            ],
            "modifierGroups": [
              {
                "modifiers": [
                  {
                    "field": "pts-id", "type": "increment", "value": 5,
                    "repeats": [
                      { "childId": "grp-weapons", "field": "selections", "repeats": 1, "value": 1, "includeChildSelections": true }
                    ]
                  },
                  {
                    "field": "pts-id", "type": "decrement", "value": 10,
                    "conditions": [
                      { "childId": "grp-weapons", "field": "selections", "type": "atLeast", "value": 2, "includeChildSelections": true }
                    ]
                  }
                ]
              }
            ]
          },
          {
            "id": "u-plain", "name": "Plain Boss", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-plain", "name": "Plain Boss", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "5\"" }, { "name": "W", "$text": "5" }]
              }
            ],
            "entryLinks": [
              { "id": "el-w2", "name": "Weapons", "type": "selectionEntryGroup", "targetId": "grp-weapons" }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Crucible_unit_surfaces_group_linked_weapon_options()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var cru = model.Datasheets.Single(d => d.Id == "u-cru");
        var optionIds = cru.Options.Select(o => o.Id).ToList();

        Assert.Contains("w-choppa", optionIds);
        Assert.Contains("w-shoota", optionIds);
        Assert.Contains("w-klaw", optionIds);
    }

    [Fact]
    public void Crucible_unit_has_per_selection_and_threshold_cost_rules()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);
        var cru = model.Datasheets.Single(d => d.Id == "u-cru");

        var perSelection = Assert.Single(cru.CostRules.Where(r => r.PerSelection));
        Assert.Equal(5, perSelection.Amount);
        Assert.Equal(1, perSelection.Step);
        Assert.Contains("w-choppa", perSelection.CountedOptionIds);
        Assert.Contains("w-shoota", perSelection.CountedOptionIds);

        var threshold = Assert.Single(cru.CostRules.Where(r => !r.PerSelection));
        Assert.Equal(-10, threshold.Amount);
        Assert.Equal("atLeast", threshold.Operator);
        Assert.Equal(2, threshold.Value);
        Assert.Contains("w-choppa", threshold.CountedOptionIds);
    }

    [Fact]
    public void Plain_unit_ignores_group_links_and_has_no_cost_rules()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);
        var plain = model.Datasheets.Single(d => d.Id == "u-plain");

        Assert.DoesNotContain("w-choppa", plain.Options.Select(o => o.Id));
        Assert.Empty(plain.CostRules);
    }
}
