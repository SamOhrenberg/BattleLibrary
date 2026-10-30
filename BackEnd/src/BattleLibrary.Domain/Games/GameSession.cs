using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Games;

// A live, multiplayer game session (up to 4 players). Distinct from the simple
// Game history log: this is the shared, round-by-round board players drive in
// real time, and it writes Game history rows for each player when it finishes.
public class GameSession : Entity
{
    public Guid CreatedByUserId { get; set; }
    public string? Name { get; set; }
    public string GameSystemKey { get; set; } = string.Empty;
    public int? PointsLimit { get; set; }

    // Battle size (Incursion / Strike Force / Onslaught); null for a custom or
    // point-less game. Sets the points cap and filters the lists players can bring.
    public string? BattleSize { get; set; }

    // CP each player begins the game with. CP gained is tracked per round; CP
    // spent is the sum of each player's logged stratagem/ability costs.
    public int StartingCp { get; set; }

    public GameSessionStatus Status { get; set; } = GameSessionStatus.Setup;
    public int CurrentRound { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public List<GameParticipant> Participants { get; set; } = [];
}
