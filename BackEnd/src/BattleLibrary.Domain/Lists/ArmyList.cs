using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

// A freeform army list, like one written on paper. Every field beyond the name
// is optional and nothing is validated against game rules.
public class ArmyList : Entity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    // One of GameSystems.Keys.
    public string GameSystemKey { get; set; } = GameSystems.Other;

    public string FactionName { get; set; } = string.Empty;
    public string? Detachment { get; set; }
    public int? PointsLimit { get; set; }
    public string? Notes { get; set; }

    // Highlighted on the owner's public profile.
    public bool Featured { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Named sections of the list (e.g. main force, allies); the first is primary.
    public List<ListForce> Forces { get; set; } = [];

    public List<ListEntry> Entries { get; set; } = [];
}
