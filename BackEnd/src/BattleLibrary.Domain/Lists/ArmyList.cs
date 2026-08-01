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
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ListEntry> Entries { get; set; } = [];
}
