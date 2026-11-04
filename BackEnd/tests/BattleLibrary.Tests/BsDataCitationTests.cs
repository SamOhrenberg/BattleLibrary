using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic catalogues proving that publication
// citation metadata (publications + per-profile/rule publicationId / page) is
// parsed and surfaced on the datasheet and its abilities.
public class BsDataCitationTests
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
        "publications": [
          { "id": "pub-codex", "name": "Codex: Orks", "shortName": "Orks" },
          { "id": "pub-core", "name": "Core Rules" }
        ],
        "forceEntries": [{ "id": "force1", "name": "Army Roster" }],
        "sharedRules": [
          {
            "id": "rule-waaagh", "name": "Waaagh!", "description": "Call a Waaagh!.",
            "publicationId": "pub-core", "page": "7"
          }
        ]
      }
    }
    """;

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
                "publicationId": "pub-codex", "page": "42",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "5" }]
              },
              {
                "id": "p-abil", "name": "Brutal", "typeId": "pt-abil", "typeName": "Abilities",
                "publicationId": "pub-codex", "page": "43",
                "characteristics": [{ "name": "Description", "$text": "Hits hard." }]
              }
            ],
            "infoLinks": [
              { "id": "il-1", "name": "Waaagh!", "type": "rule", "targetId": "rule-waaagh" }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Datasheet_source_cites_book_shortname_and_page()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boss = Assert.Single(model.Datasheets);
        Assert.NotNull(boss.Source);
        Assert.Equal("Orks", boss.Source!.Publication);
        Assert.Equal("42", boss.Source.Page);
    }

    [Fact]
    public void Ability_from_profile_cites_its_page()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boss = Assert.Single(model.Datasheets);
        var brutal = boss.Abilities.Single(a => a.Name == "Brutal");
        Assert.NotNull(brutal.Source);
        Assert.Equal("Orks", brutal.Source!.Publication);
        Assert.Equal("43", brutal.Source.Page);
    }

    [Fact]
    public void Ability_from_rule_cites_full_title_when_no_shortname()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boss = Assert.Single(model.Datasheets);
        var waaagh = boss.Abilities.Single(a => a.Name == "Waaagh!");
        Assert.NotNull(waaagh.Source);
        Assert.Equal("Core Rules", waaagh.Source!.Publication);
        Assert.Equal("7", waaagh.Source.Page);
    }

    // A system + catalogue carrying no publications and no publicationId / page
    // anywhere: nothing should be invented.
    private const string UncitedSystem = """
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
        "sharedRules": [
          { "id": "rule-waaagh", "name": "Waaagh!", "description": "Call a Waaagh!." }
        ]
      }
    }
    """;

    private const string UncitedCatalogue = """
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
              },
              {
                "id": "p-abil", "name": "Brutal", "typeId": "pt-abil", "typeName": "Abilities",
                "characteristics": [{ "name": "Description", "$text": "Hits hard." }]
              }
            ],
            "infoLinks": [
              { "id": "il-1", "name": "Waaagh!", "type": "rule", "targetId": "rule-waaagh" }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Uncited_content_has_no_source()
    {
        var model = new BsDataParser().ParseCatalogue(UncitedSystem, UncitedCatalogue);

        var boss = Assert.Single(model.Datasheets);
        Assert.Null(boss.Source);
        Assert.All(boss.Abilities, a => Assert.Null(a.Source));
    }
}
