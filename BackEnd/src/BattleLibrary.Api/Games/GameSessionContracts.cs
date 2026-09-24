namespace BattleLibrary.Api.Games;

// ---- Requests ----

public sealed record CreateSessionRequest(string? Name, string? GameSystemKey, int? PointsLimit);

public sealed record InviteRequest(string Username);

public sealed record RespondRequest(bool Accept);

public sealed record SetListRequest(Guid? ListId);

public sealed record UpdateUnitRequest(int? StartingModels, int? CurrentModels, bool? Destroyed);

public sealed record SecondaryInput(string Name, int Score);

public sealed record RoundScoreRequest(
    int PrimaryScore,
    int CpGained,
    int CpSpent,
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
    int StartingModels,
    int CurrentModels,
    bool Destroyed);

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
    IReadOnlyList<ActionDto> Actions);

public sealed record SessionDto(
    Guid Id,
    string? Name,
    string GameSystemKey,
    int? PointsLimit,
    string Status,
    int CurrentRound,
    Guid CreatedByUserId,
    bool IsHost,
    int MaxParticipants,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ParticipantDto> Participants);
