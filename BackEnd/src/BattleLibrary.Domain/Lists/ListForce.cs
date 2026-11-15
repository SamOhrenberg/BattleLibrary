using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// A named section of an army list. Entries are grouped by ListForceId.
public class ListForce : Entity
{
    public Guid ArmyListId { get; set; }
    public ArmyList ArmyList { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    // Display order within the list; the primary section is 0.
    public int SortOrder { get; set; }
}
