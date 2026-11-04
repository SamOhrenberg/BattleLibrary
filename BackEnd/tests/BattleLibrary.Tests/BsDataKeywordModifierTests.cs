using System.Linq;
using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic catalogues proving that "category"
// modifiers are honoured - unconditional add/remove folded into the static
// keyword set, conditional (detachment-gated) changes surfaced as
// ConditionalKeywords, and dynamic conditions left out.
public class BsDataKeywordModifierTests
{
    private const string GameSystem = """
    {
      "gameSystem": {
        "id": "sys1",
        "name": "Test System",
        "revision": 1,
        "costTypes": [
          { "id": "pts-id", "name": "pts" },
          { "id": "enh-id", "name": "Enhancements" },
          { "id": "dp-id", "name": "Detachment Points" }
        ],
        "profileTypes": [
          { "id": "pt-unit", "name": "Unit" },
          { "id": "pt-abil", "name": "Abilities" }
        ],
        "categoryEntries": [
          { "id": "cat-infantry", "name": "Infantry" },
          { "id": "cat-battleline", "name": "Battleline" },
          { "id": "cat-mob", "name": "Mob" }
        ],
        "forceEntries": [{ "id": "force1", "name": "Army Roster" }]
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
        "sharedSelectionEntryGroups": [
          {
            "id": "grp-det", "name": "Detachment",
            "selectionEntries": [
              {
                "id": "det-warhorde", "name": "War Horde", "type": "upgrade",
                "costs": [{ "name": "Detachment Points", "typeId": "dp-id", "value": 3 }]
              }
            ]
          }
        ],
        "sharedSelectionEntries": [
          {
            "id": "unit-gretchin", "name": "Gretchin", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 40 }],
            "profiles": [
              {
                "id": "p-gret", "name": "Gretchin", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "1" }]
              }
            ],
            "categoryLinks": [
              { "id": "cl-1", "name": "Infantry", "targetId": "cat-infantry" },
              { "id": "cl-2", "name": "Battleline", "targetId": "cat-battleline" }
            ],
            "modifiers": [
              { "type": "remove", "field": "category", "value": "cat-battleline" },
              { "type": "add", "field": "category", "value": "cat-mob" }
            ]
          },
          {
            "id": "unit-boyz", "name": "Boyz", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-boyz", "name": "Boyz", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "1" }]
              }
            ],
            "categoryLinks": [
              { "id": "cl-3", "name": "Infantry", "targetId": "cat-infantry" }
            ],
            "modifiers": [
              {
                "type": "add", "field": "category", "value": "cat-battleline",
                "conditions": [
                  { "type": "atLeast", "value": 1, "field": "selections", "childId": "det-warhorde", "scope": "roster" }
                ]
              }
            ]
          },
          {
            "id": "unit-stormboyz", "name": "Stormboyz", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 90 }],
            "profiles": [
              {
                "id": "p-storm", "name": "Stormboyz", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "W", "$text": "1" }]
              }
            ],
            "categoryLinks": [
              { "id": "cl-4", "name": "Infantry", "targetId": "cat-infantry" }
            ],
            "modifiers": [
              {
                "type": "add", "field": "category", "value": "cat-mob",
                "conditions": [
                  { "type": "atLeast", "value": 3, "field": "selections", "childId": "unit-boyz", "scope": "roster" }
                ]
              }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void Folds_unconditional_category_modifiers_into_static_keywords()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var gretchin = model.Datasheets.Single(d => d.Name == "Gretchin");

        // "remove" strips a keyword carried by a category link; "add" grants one.
        Assert.Contains("Infantry", gretchin.Keywords);
        Assert.Contains("Mob", gretchin.Keywords);
        Assert.DoesNotContain("Battleline", gretchin.Keywords);

        // Nothing conditional on this unit.
        Assert.Empty(gretchin.ConditionalKeywords);
    }

    [Fact]
    public void Captures_detachment_gated_keyword_as_conditional()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var boyz = model.Datasheets.Single(d => d.Name == "Boyz");

        // The static set is unchanged - Battleline is only gained under a detachment.
        Assert.Contains("Infantry", boyz.Keywords);
        Assert.DoesNotContain("Battleline", boyz.Keywords);

        var conditional = Assert.Single(boyz.ConditionalKeywords);
        Assert.Equal("Battleline", conditional.Keyword);
        Assert.True(conditional.Add);
        Assert.Contains("det-warhorde", conditional.RequiredDetachmentIds);
        Assert.Empty(conditional.RequiredToggles);

        // Sanity: the detachment the keyword is gated on is a real parsed detachment.
        Assert.Contains(model.Detachments, d => d.Id == "det-warhorde" && d.Name == "War Horde");
    }

    [Fact]
    public void Ignores_dynamic_conditions_that_cannot_be_modelled()
    {
        var model = new BsDataParser().ParseCatalogue(GameSystem, Catalogue);

        var stormboyz = model.Datasheets.Single(d => d.Name == "Stormboyz");

        // Gated on a roster selection count (not a detachment or config toggle),
        // so it is neither folded into the static set nor surfaced conditionally.
        Assert.DoesNotContain("Mob", stormboyz.Keywords);
        Assert.Empty(stormboyz.ConditionalKeywords);
    }
}
