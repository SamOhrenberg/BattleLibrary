namespace BattleLibrary.Api.Games;

// Opponent is given either as OpponentUserId (a registered player) or a
// free-text OpponentName (a stranger). PlayedOn defaults to today when omitted.
public sealed record RecordGameRequest(
    DateOnly? PlayedOn,
    string? GameSystemKey,
    string Result,
    Guid? OpponentUserId,
    string? OpponentName,
    string? PlayerFactionName,
    string? OpponentFactionName,
    Guid? PlayerListId,
    int? PlayerScore,
    int? OpponentScore,
    string? Notes);

public sealed record GameDto(
    Guid Id,
    DateOnly PlayedOn,
    string GameSystemKey,
    string Result,
    Guid? OpponentUserId,
    string OpponentName,
    string? PlayerFactionName,
    string? OpponentFactionName,
    Guid? PlayerListId,
    string? PlayerListName,
    int? PlayerScore,
    int? OpponentScore,
    string? Notes,
    Guid? GameSessionId);

public sealed record GameStatsDto(
    int Total,
    int Wins,
    int Losses,
    int Draws,
    double WinRate);
