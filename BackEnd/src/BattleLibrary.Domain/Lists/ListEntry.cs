using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

public class ListEntry : Entity
{
    public Guid ArmyListId { get; set; }
    public ArmyList ArmyList { get; set; } = null!;

    // Reference to the catalogue datasheet, with a points/name snapshot taken
    // when the entry was added so the list total is stable.
    public string DatasheetId { get; set; } = string.Empty;
    public string DatasheetName { get; set; } = string.Empty;

    // Points for the chosen unit size, snapshotted at add-time.
    public int Points { get; set; }

    // Chosen model count (the unit's size); drives Points for multi-size units.
    public int ModelCount { get; set; } = 1;

    public int Quantity { get; set; } = 1;

    // Datasheet keywords snapshotted at add-time, for list-legality checks
    // (CHARACTER / EPIC HERO / BATTLELINE / DEDICATED TRANSPORT).
    public List<string> Keywords { get; set; } = [];

    // Army's warlord (exactly one CHARACTER across the list).
    public bool IsWarlord { get; set; }

    // Enhancement assigned to this character, snapshotting id/name/points.
    public string? EnhancementId { get; set; }
    public string? EnhancementName { get; set; }
    public int EnhancementPoints { get; set; }

    // Chosen wargear options, each snapshotting its id/name/points at add-time.
    public List<SelectedOption> SelectedOptions { get; set; } = [];
}

public class SelectedOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }
}
