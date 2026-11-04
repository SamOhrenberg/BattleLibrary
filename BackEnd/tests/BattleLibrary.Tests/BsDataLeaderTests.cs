using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): a synthetic catalogue proving the 11e
// leader-attachment mechanic is parsed - a character's "associations" become
// the list of units it can lead, drawn from the "instanceOf" conditions that
// reference a unit category (flat or grouped), while wargear prerequisites and
// other condition types are excluded.
public class BsDataLeaderTests
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

    // Two category ids name the boss's two bodyguard units; a third condition is
    // a wargear prerequisite (an entry, not a category) that must not be a target.
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "categoryEntries": [
          { "id": "cat-boyz", "name": "Boyz" },
          { "id": "cat-nobz", "name": "Nobz" }
        ],
        "sharedSelectionEntries": [
          {
            "id": "unit-warboss", "name": "Warboss", "type": "model",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-warboss", "name": "Warboss", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }]
              }
            ],
            "associations": [
              {
                "id": "assoc-1", "label": "Leader", "action": "group",
                "childId": "unit", "childName": "unit", "scope": "force", "min": 0, "max": 1,
                "conditions": [
                  { "type": "instanceOf", "value": 1, "field": "selections", "scope": "self", "childId": "cat-boyz", "childName": "Boyz" },
                  { "type": "atLeast", "value": 1, "field": "selections", "scope": "self", "childId": "up-banner", "childName": "Banner" }
                ],
                "conditionGroups": [
                  {
                    "type": "or",
                    "conditions": [
                      { "type": "instanceOf", "value": 1, "field": "selections", "scope": "self", "childId": "cat-nobz", "childName": "Nobz" }
                    ]
                  }
                ]
              }
            ]
          },
          {
            "id": "unit-boyz", "name": "Boyz", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-boyz", "name": "Boyz", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }]
              }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Leader_lists_the_units_it_can_attach_to()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var warboss = Assert.Single(model.Datasheets, d => d.Name == "Warboss");
        var attachment = Assert.Single(warboss.LeaderAttachments);

        Assert.Equal("Leader", attachment.Label);
        // Both the flat and the OR-grouped category conditions are targets.
        Assert.Contains("Boyz", attachment.Targets);
        Assert.Contains("Nobz", attachment.Targets);
        // The wargear prerequisite (an entry, not a category) is not a target.
        Assert.DoesNotContain("Banner", attachment.Targets);
    }

    [Fact]
    public void Non_leader_datasheet_has_no_attachments()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boyz = Assert.Single(model.Datasheets, d => d.Name == "Boyz");
        Assert.Empty(boyz.LeaderAttachments);
    }
}
