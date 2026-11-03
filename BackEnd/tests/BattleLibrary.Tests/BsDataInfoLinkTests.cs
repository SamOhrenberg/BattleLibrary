using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic catalogues proving that abilities
// attached by reference resolve - profile infoLinks, rule infoLinks, and info
// groups - and that hidden references are skipped.
public class BsDataInfoLinkTests
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
          { "id": "pt-abil", "name": "Abilities" }
        ],
        "forceEntries": [{ "id": "force1", "name": "Army Roster" }],
        "sharedProfiles": [
          {
            "id": "prof-leader", "name": "Leader", "typeId": "pt-abil", "typeName": "Abilities",
            "characteristics": [{ "name": "Description", "$text": "Attach to a Bodyguard unit." }]
          }
        ],
        "sharedRules": [
          { "id": "rule-waaagh", "name": "Waaagh!", "description": "Once per battle, call a Waaagh!." },
          { "id": "rule-hidden", "name": "Secret Rule", "description": "Should not appear." }
        ],
        "sharedInfoGroups": [
          {
            "id": "ig-core", "name": "Core",
            "profiles": [
              {
                "id": "prof-fnp", "name": "Feel No Pain", "typeId": "pt-abil", "typeName": "Abilities",
                "characteristics": [{ "name": "Description", "$text": "Ignore some wounds." }]
              }
            ]
          }
        ]
      }
    }
    """;

    // The boss carries no inline Abilities profile; every ability is by reference.
    private const string Catalogue = """
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntries": [
          {
            "id": "unit1", "name": "Test Boss", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-stat", "name": "Test Boss", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "5" }]
              }
            ],
            "infoLinks": [
              { "id": "il-1", "name": "Leader", "type": "profile", "targetId": "prof-leader" },
              { "id": "il-2", "name": "Waaagh!", "type": "rule", "targetId": "rule-waaagh" },
              { "id": "il-3", "name": "Core", "type": "infoGroup", "targetId": "ig-core" },
              { "id": "il-4", "name": "Secret Rule", "type": "rule", "targetId": "rule-hidden", "hidden": true }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Resolves_abilities_from_profile_rule_and_infogroup_links()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boss = Assert.Single(model.Datasheets);
        var abilities = boss.Abilities.Select(a => a.Name).ToList();

        Assert.Contains("Leader", abilities);        // profile infoLink
        Assert.Contains("Waaagh!", abilities);        // rule infoLink
        Assert.Contains("Feel No Pain", abilities);   // info-group-contained profile

        var leader = boss.Abilities.Single(a => a.Name == "Leader");
        Assert.Equal("Attach to a Bodyguard unit.", leader.Text);
        var waaagh = boss.Abilities.Single(a => a.Name == "Waaagh!");
        Assert.Equal("Once per battle, call a Waaagh!.", waaagh.Text);
    }

    [Fact]
    public void Skips_hidden_info_references()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boss = Assert.Single(model.Datasheets);
        Assert.DoesNotContain("Secret Rule", boss.Abilities.Select(a => a.Name));
    }
}
