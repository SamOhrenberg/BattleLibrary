using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

public class ListEntry : Entity
{
    public Guid ArmyListId { get; set; }
    public ArmyList ArmyList { get; set; } = null!;

    // The force (faction slice) this entry belongs to. A grouping key into the
    // list's ListForces, not a navigation; entries still cascade via ArmyListId.
    public Guid ListForceId { get; set; }

    // Reference to the catalogue datasheet, with a points/name snapshot taken
    // when the entry was added so the list total is stable.
    public string DatasheetId { get; set; } = string.Empty;
    public string DatasheetName { get; set; } = string.Empty;

    // Optional player-given name for this unit (e.g. a character or squad name);
    // null means show the datasheet name. Exported as the selection's customName.
    public string? CustomName { get; set; }

    // Optional free-text note the owner keeps against this unit.
    public string? Notes { get; set; }

    // Points for the chosen unit size, snapshotted at add-time.
    public int Points { get; set; }

    // Chosen model count (the unit's size); drives Points for multi-size units.
    public int ModelCount { get; set; } = 1;

    public int Quantity { get; set; } = 1;

    // Position in the owner's custom ordering of the list; new entries append.
    // Ties fall back to datasheet name, so pre-existing lists keep their order.
    public int SortOrder { get; set; }

    // Datasheet keywords snapshotted at add-time, for list-legality checks
    // (CHARACTER / EPIC HERO / BATTLELINE / DEDICATED TRANSPORT).
    public List<string> Keywords { get; set; } = [];

    // Army's warlord (exactly one CHARACTER across the list).
    public bool IsWarlord { get; set; }

    // Leader attachment: when set on a Character (the Leader), the id of the
    // non-Character unit (the Bodyguard) it is attached to in this list. Null for
    // a standalone unit. Cleared if the bodyguard is removed.
    public Guid? AttachedToEntryId { get; set; }

    // Set when the datasheet's rules require this unit to be the Warlord; its
    // Warlord status is then locked and two such units in one list is illegal.
    public bool ForcedWarlord { get; set; }

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
