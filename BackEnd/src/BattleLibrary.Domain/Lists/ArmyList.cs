using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Lists;

public class ArmyList : Entity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    public string GameSystemKey { get; set; } = string.Empty;
    public string FactionId { get; set; } = string.Empty;
    public string FactionName { get; set; } = string.Empty;

    public int? PointsLimit { get; set; }

    // 40k battle size (e.g. "Strike Force"); sets the points limit. Null otherwise.
    public string? BattleSize { get; set; }

    // Chosen detachments (11e Detachment Points budget); each snapshots id/name/dp.
    public List<ListDetachment> Detachments { get; set; } = [];

    // Highlighted on the owner's public profile.
    public bool Featured { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ListEntry> Entries { get; set; } = [];
}

public class ListDetachment
{
    public string DetachmentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Dp { get; set; }
}
