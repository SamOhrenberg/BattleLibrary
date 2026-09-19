namespace BattleLibrary.Domain.Games;

public enum GameSessionStatus
{
    Setup = 0,
    Active = 1,
    Finished = 2,
    Abandoned = 3,
}

public enum ParticipantStatus
{
    Invited = 0,
    Accepted = 1,
    Declined = 2,
    Left = 3,
}

public enum GameActionKind
{
    Ability = 0,
    Stratagem = 1,
}
