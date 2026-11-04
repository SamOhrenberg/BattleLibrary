using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): a synthetic game system shaped like wh40k-11e's
// "Army Roster" force - base max constraints for points / Detachment Points /
// Enhancements, plus "set" modifiers that raise those caps per the selected
// battle size. Proves the parser derives the per-battle-size caps from the data
// instead of hardcoding them, so the one authoritative source is the catalogue.
public class RosterLimitsTests
{
    // pts/DP/Enhancements cost types; the force's three max constraints carry an
    // id each so modifiers can target them. Battle sizes are config entries the
    // modifiers are gated on (atLeast/equalTo 1 = selected). The shape mirrors the
    // real data: DP 3 under Strike Force (or Incursion + a 3DP detachment), DP 4
    // under Onslaught, and Enhancements 4 whenever the size is not Incursion.
    private const string GameSystem = """
    {
      "gameSystem": {
        "id": "sys1", "name": "Test System", "revision": 1,
        "costTypes": [
          { "id": "pts", "name": "pts", "defaultCostLimit": -1 },
          { "id": "dp", "name": "Detachment Points", "defaultCostLimit": -1 },
          { "id": "enh", "name": "Enhancements", "defaultCostLimit": 0 }
        ],
        "forceEntries": [
          {
            "id": "force1", "name": "Army Roster",
            "categoryLinks": [
              {
                "id": "clc", "name": "Character", "targetId": "cat-char",
                "constraints": [
                  { "id": "cc1", "field": "selections", "scope": "roster", "type": "min", "value": 1 }
                ]
              },
              {
                "id": "clf", "name": "Fortification", "targetId": "cat-fort",
                "constraints": [
                  { "id": "cf1", "field": "selections", "scope": "force", "type": "max", "value": 3 }
                ]
              }
            ],
            "constraints": [
              { "id": "c-dp",  "field": "dp",  "scope": "parent", "type": "max", "value": 2 },
              { "id": "c-pts", "field": "pts", "scope": "parent", "type": "max", "value": 0 },
              { "id": "c-enh", "field": "enh", "scope": "force",  "type": "max", "value": 2 }
            ],
            "modifiers": [
              { "field": "c-pts", "type": "set", "value": 1000,
                "conditions": [
                  { "childId": "bs-inc", "childName": "1. Incursion (1000 Point limit)", "type": "atLeast", "value": 1 },
                  { "childId": "ovr", "childName": "Override points limit?", "type": "equalTo", "value": 0 }
                ] },
              { "field": "c-pts", "type": "set", "value": 2000,
                "conditions": [
                  { "childId": "bs-sf", "childName": "2. Strike Force (2000 Point limit)", "type": "atLeast", "value": 1 },
                  { "childId": "ovr", "childName": "Override points limit?", "type": "equalTo", "value": 0 }
                ] },
              { "field": "c-pts", "type": "set", "value": 3000,
                "conditions": [
                  { "childId": "bs-ons", "childName": "3. Onslaught (3000 Point limit)", "type": "atLeast", "value": 1 },
                  { "childId": "ovr", "childName": "Override points limit?", "type": "equalTo", "value": 0 }
                ] },
              { "field": "c-pts", "type": "increment", "value": 1,
                "repeats": [ { "childId": "ptslimit", "field": "selections", "type": "instanceOf", "value": 1 } ] },
              { "field": "c-dp", "type": "set", "value": 3,
                "conditionGroups": [
                  { "type": "or",
                    "conditions": [ { "childId": "bs-sf", "type": "equalTo", "value": 1 } ],
                    "conditionGroups": [
                      { "type": "and", "conditions": [
                        { "childId": "bs-inc", "type": "equalTo", "value": 1 },
                        { "childId": "t-3dp", "type": "atLeast", "value": 1 }
                      ] }
                    ] }
                ] },
              { "field": "c-dp", "type": "set", "value": 4,
                "conditions": [ { "childId": "bs-ons", "type": "equalTo", "value": 1 } ] },
              { "field": "c-enh", "type": "set", "value": 4,
                "conditions": [ { "childId": "bs-inc", "type": "equalTo", "value": 0 } ] }
            ]
          }
        ]
      }
    }
    """;

    private const string Catalogue = """
    { "catalogue": { "id": "cat1", "name": "Test Faction", "revision": 1, "gameSystemId": "sys1" } }
    """;

    private static Domain.Catalogue.RosterLimits Parse() =>
        new BsDataParser().ParseCatalogue(GameSystem, Catalogue).GameSystem.Limits
        ?? Domain.Catalogue.RosterLimits.None;

    [Fact]
    public void Derives_a_cap_set_for_each_battle_size()
    {
        var names = Parse().BattleSizes.Select(s => s.Name).ToArray();

        Assert.Equal(new[] { "Incursion", "Strike Force", "Onslaught" }, names);
    }

    [Theory]
    [InlineData("Incursion", 1000, 2, 2)]      // base DP/enh; not raised at the smallest size
    [InlineData("Strike Force", 2000, 3, 4)]
    [InlineData("Onslaught", 3000, 4, 4)]      // DP cap is 4 (per data), NOT the old hardcoded 5
    public void Derives_points_dp_and_enhancement_caps_from_the_force_modifiers(
        string name, int pts, int dp, int enh)
    {
        var size = Parse().BattleSizes.Single(s => s.Name == name);

        Assert.Equal(pts, size.PointsCap);
        Assert.Equal(dp, size.DetachmentPointsCap);
        Assert.Equal(enh, size.EnhancementCap);
    }

    [Fact]
    public void Onslaught_detachment_points_cap_comes_from_data_not_the_placeholder()
    {
        // Regression guard for the M1 "placeholder (TBC)" finding: validation.ts
        // hardcoded Onslaught at 5 DP, but the upstream data caps it at 4.
        var onslaught = Parse().BattleSizes.Single(s => s.Name == "Onslaught");

        Assert.Equal(4, onslaught.DetachmentPointsCap);
        Assert.NotEqual(5, onslaught.DetachmentPointsCap);
    }

    [Fact]
    public void Reads_per_category_caps_from_force_category_links()
    {
        var limits = Parse().CategoryLimits;

        var character = limits.Single(c => c.Name == "Character");
        Assert.Equal(1, character.Min);
        Assert.Null(character.Max);

        var fortification = limits.Single(c => c.Name == "Fortification");
        Assert.Null(fortification.Min);
        Assert.Equal(3, fortification.Max);
    }

    [Fact]
    public void Pointless_system_has_no_roster_limits()
    {
        // Kill Team-style: no "pts" cost type, so no budgets or battle sizes.
        const string pointless = """
        {
          "gameSystem": {
            "id": "kt", "name": "Kill Team", "revision": 1,
            "costTypes": [],
            "forceEntries": [ { "id": "f", "name": "Kill Team" } ]
          }
        }
        """;
        const string ktCat = """
        { "catalogue": { "id": "c", "name": "Faction", "revision": 1, "gameSystemId": "kt" } }
        """;

        var limits = new BsDataParser().ParseCatalogue(pointless, ktCat, schema: CatalogueSchema.KillTeam2024)
            .GameSystem.Limits ?? Domain.Catalogue.RosterLimits.None;

        Assert.Empty(limits.BattleSizes);
        Assert.Empty(limits.CategoryLimits);
    }
}
