using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// A saved wargear/size loadout for one datasheet, reusable when adding that unit
// to any of the owner's lists. Stored per user and keyed to the game system +
// datasheet so it only surfaces for the matching unit.
public class LoadoutPreset : Entity
{
    public Guid UserId { get; set; }

    public string GameSystemKey { get; set; } = string.Empty;
    public string DatasheetId { get; set; } = string.Empty;

    // Datasheet name snapshotted for display without a catalogue lookup.
    public string DatasheetName { get; set; } = string.Empty;

    // Player-given label for the loadout (e.g. "Melta brick").
    public string Name { get; set; } = string.Empty;

    // Chosen unit size and its base points, snapshotted at save-time.
    public int ModelCount { get; set; } = 1;
    public int Points { get; set; }

    // Chosen wargear options, each snapshotting id/name/points.
    public List<SelectedOption> SelectedOptions { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
