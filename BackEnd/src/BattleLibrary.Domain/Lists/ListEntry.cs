using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// One unit in a list, entered by hand. Only the name is expected; points,
// models, wargear, enhancement and notes are optional free input.
public class ListEntry : Entity
{
    public Guid ArmyListId { get; set; }
    public ArmyList ArmyList { get; set; } = null!;

    // The section this entry belongs to. A grouping key, not a navigation.
    public Guid ListForceId { get; set; }

    public string Name { get; set; } = string.Empty;
    public int? Points { get; set; }
    public int? ModelCount { get; set; }
    public string? Wargear { get; set; }
    public string? Enhancement { get; set; }
    public string? Notes { get; set; }

    // Position in the owner's custom ordering of the list.
    public int SortOrder { get; set; }

    public bool IsWarlord { get; set; }

    // The entry this one is attached to (e.g. a leader joining a unit); null when
    // standalone. Cleared if the target is removed.
    public Guid? AttachedToEntryId { get; set; }
}
