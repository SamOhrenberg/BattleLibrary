namespace BattleLibrary.Api.Games;

// ---- Requests ----

public sealed record CreateSessionRequest(string? Name, string? GameSystemKey, int? PointsLimit, string? BattleSize, int? StartingCp);

public sealed record InviteRequest(string Username);

public sealed record RespondRequest(bool Accept);

public sealed record SetListRequest(Guid? ListId);

public sealed record UpdateUnitRequest(int? StartingModels, int? CurrentModels, bool? Destroyed);

public sealed record SecondaryInput(string Name, int Score);

public sealed record RoundScoreRequest(
    int PrimaryScore,
    int CpGained,
    IReadOnlyList<SecondaryInput>? Secondaries);

public sealed record LogActionRequest(int? RoundNumber, string Kind, string Name, int? CpCost, string? Note);

// ---- DTOs ----

public sealed record SessionSummaryDto(
    Guid Id,
    string? Name,
    string GameSystemKey,
    int? PointsLimit,
    string Status,
    int CurrentRound,
    bool IsHost,
    string MyStatus,
    int ParticipantCount,
    DateTimeOffset CreatedAt);

public sealed record UnitDto(
    Guid Id,
    string DatasheetId,
    string Name,
    int MaxModels,
    int StartingModels,
    int CurrentModels,
    bool Destroyed,
    IReadOnlyList<string> Keywords,
    bool IsWarlord,
    string? EnhancementName,
    int Points,
    IReadOnlyList<string> Wargear);

public sealed record SecondaryDto(string Name, int Score);

public sealed record RoundDto(
    int RoundNumber,
    int PrimaryScore,
    int CpGained,
    int CpSpent,
    IReadOnlyList<SecondaryDto> Secondaries,
    int SecondaryTotal,
    int RoundTotal);

public sealed record ActionDto(Guid Id, int RoundNumber, string Kind, string Name, int? CpCost, string? Note);

public sealed record ParticipantDto(
    Guid Id,
    Guid UserId,
    string Username,
    string Status,
    int Seat,
    Guid? ArmyListId,
    string? FactionName,
    bool IsMe,
    int TotalScore,
    int CurrentCp,
    int UnitsTotal,
    int UnitsDestroyed,
    IReadOnlyList<UnitDto> Units,
    IReadOnlyList<RoundDto> Rounds,
    IReadOnlyList<ActionDto> Actions,
    // Setup-only: why this player's chosen list can't be used, else null.
    string? ListIssue);

public sealed record SessionDto(
    Guid Id,
    string? Name,
    string GameSystemKey,
    int? PointsLimit,
    string? BattleSize,
    int StartingCp,
    string Status,
    int CurrentRound,
    // The standard battle-round count this game runs to. The host advances up to
    // this cap and can finish earlier to end the game short.
    int MaxRounds,
    Guid CreatedByUserId,
    bool IsHost,
    int MaxParticipants,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ParticipantDto> Participants);
