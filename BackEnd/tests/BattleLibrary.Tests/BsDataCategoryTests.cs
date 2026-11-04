using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Self-contained (no GW data): synthetic catalogues proving that the category
// flagged primary is surfaced as the datasheet's PrimaryKeyword, while still
// appearing in the full keyword list alongside the rest.
public class BsDataCategoryTests
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

    private static string CatalogueWith(string categoryLinks) => $$"""
    {
      "catalogue": {
        "id": "cat1",
        "name": "Test Faction",
        "revision": 1,
        "gameSystemId": "sys1",
        "sharedSelectionEntries": [
          {
            "id": "unit1", "name": "Test Unit", "type": "unit",
            "costs": [{ "name": "pts", "typeId": "pts-id", "value": 80 }],
            "profiles": [
              {
                "id": "p-stat", "name": "Test Unit", "typeId": "pt-unit", "typeName": "Unit",
                "characteristics": [{ "name": "M", "$text": "6\"" }, { "name": "W", "$text": "5" }]
              }
            ],
            "categoryLinks": {{categoryLinks}}
          }
        ]
      }
    }
    """;

    [Fact]
    public void Surfaces_primary_category_as_primary_keyword()
    {
        var catalogue = CatalogueWith("""
        [
          { "id": "cl-1", "name": "Infantry", "targetId": "cat-inf", "primary": false },
          { "id": "cl-2", "name": "Battleline", "targetId": "cat-bl", "primary": true },
          { "id": "cl-3", "name": "Ork", "targetId": "cat-ork", "primary": false }
        ]
        """);

        var model = new BsDataParser().ParseCatalogue(GameSystem, catalogue);

        var unit = Assert.Single(model.Datasheets);
        Assert.Equal("Battleline", unit.PrimaryKeyword);

        // The primary category also stays in the full keyword list, next to the
        // rest, and is ordered first so role-grouping consumers can read it off
        // the front.
        Assert.Equal("Battleline", unit.Keywords[0]);
        Assert.Contains("Infantry", unit.Keywords);
        Assert.Contains("Ork", unit.Keywords);

        // Ordering must not duplicate the primary (it appears once, deduped).
        Assert.Single(unit.Keywords, k => string.Equals(k, "Battleline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Primary_keyword_is_empty_when_no_category_is_flagged_primary()
    {
        var catalogue = CatalogueWith("""
        [
          { "id": "cl-1", "name": "Infantry", "targetId": "cat-inf", "primary": false },
          { "id": "cl-2", "name": "Ork", "targetId": "cat-ork" }
        ]
        """);

        var model = new BsDataParser().ParseCatalogue(GameSystem, catalogue);

        var unit = Assert.Single(model.Datasheets);
        Assert.Equal("", unit.PrimaryKeyword);
        Assert.Contains("Infantry", unit.Keywords);
        Assert.Contains("Ork", unit.Keywords);
    }
}
