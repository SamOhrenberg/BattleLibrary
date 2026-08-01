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
    public int Points { get; set; }
    public int Quantity { get; set; } = 1;

    // Chosen wargear options, each snapshotting its id/name/points at add-time.
    public List<SelectedOption> SelectedOptions { get; set; } = [];
}

public class SelectedOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }
}
