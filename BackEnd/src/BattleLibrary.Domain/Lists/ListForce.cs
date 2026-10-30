using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// A force within an army list: one faction's slice of the list. A list has one
// or more forces (allied / multiple detachments); each force holds its own
// entries, grouped by its ListForceId. The list's first force (SortOrder 0) is
// the primary one and mirrors the list's FactionId/FactionName.
public class ListForce : Entity
{
    public Guid ArmyListId { get; set; }
    public ArmyList ArmyList { get; set; } = null!;

    public string FactionId { get; set; } = string.Empty;
    public string FactionName { get; set; } = string.Empty;

    // Display order of the force within the list; the primary force is 0.
    public int SortOrder { get; set; }
}
