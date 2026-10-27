using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Games;

// One player's seat in a GameSession. Units, per-round records and the action
// log hang off the participant as JSON (small, per-player, rewritten in place).
public class GameParticipant : Entity
{
    public Guid GameSessionId { get; set; }
    public GameSession GameSession { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ParticipantStatus Status { get; set; } = ParticipantStatus.Invited;
    public int Seat { get; set; }

    // The player's chosen army list (their own). Soft reference: the units below
    // are snapshotted from it, so a later list edit/delete does not change play.
    public Guid? ArmyListId { get; set; }
    public string? FactionName { get; set; }

    public List<GameUnit> Units { get; set; } = [];
    public List<GameRoundRecord> Rounds { get; set; } = [];
    public List<GameAction> Actions { get; set; } = [];
}

// A unit on the board with a model counter. Destroyed is set when models hit 0.
// MaxModels is the count the army list allows — you can bring fewer, never more.
public class GameUnit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string DatasheetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxModels { get; set; } = 1;
    public int StartingModels { get; set; } = 1;
    public int CurrentModels { get; set; } = 1;
    public bool Destroyed { get; set; }
}

// One player's scoring and CP for a single battle round.
public class GameRoundRecord
{
    public int RoundNumber { get; set; }
    public int PrimaryScore { get; set; }
    public int CpGained { get; set; }
    public int CpSpent { get; set; }
    public List<SecondaryEntry> Secondaries { get; set; } = [];
}

public class SecondaryEntry
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
}

// A logged ability or stratagem use. Stratagem text is user-entered (no GW
// rules data is ingested); CpCost is informational.
public class GameAction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int RoundNumber { get; set; }
    public GameActionKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? CpCost { get; set; }
    public string? Note { get; set; }
}
