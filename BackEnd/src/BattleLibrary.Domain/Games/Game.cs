using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;
using BattleLibrary.Domain.Lists;

namespace BattleLibrary.Domain.Games;

// A game logged by its owner against another player. The opponent is either a
// registered user (OpponentUserId set) or a stranger; OpponentName always holds
// a display name. The owner's army list is an optional soft reference.
public class Game : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateOnly PlayedOn { get; set; }
    public string GameSystemKey { get; set; } = string.Empty;

    // Owner side.
    public string? PlayerFactionName { get; set; }
    public Guid? PlayerListId { get; set; }
    public ArmyList? PlayerList { get; set; }

    // Opponent side.
    public Guid? OpponentUserId { get; set; }
    public User? OpponentUser { get; set; }
    public string OpponentName { get; set; } = string.Empty;
    public string? OpponentFactionName { get; set; }

    public GameResult Result { get; set; }
    public int? PlayerScore { get; set; }
    public int? OpponentScore { get; set; }
    public string? Notes { get; set; }

    // Set when this history row was produced by finishing a live GameSession.
    public Guid? GameSessionId { get; set; }
}
