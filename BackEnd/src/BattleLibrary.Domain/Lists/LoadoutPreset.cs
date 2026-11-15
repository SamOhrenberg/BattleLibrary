using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// A saved unit template (name and its typed fields), reusable across the owner's
// lists for the same game system.
public class LoadoutPreset : Entity
{
    public Guid UserId { get; set; }

    public string GameSystemKey { get; set; } = GameSystems.Other;

    // Player-given label for the preset (e.g. "Melta squad").
    public string Name { get; set; } = string.Empty;

    public string UnitName { get; set; } = string.Empty;
    public int? Points { get; set; }
    public int? ModelCount { get; set; }
    public string? Wargear { get; set; }
    public string? Enhancement { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
