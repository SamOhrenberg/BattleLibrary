namespace BattleLibrary.Api.Lists;

public sealed record CreateListRequest(string Name, string GameSystemKey, string FactionId, int? PointsLimit);

public sealed record UpdateListRequest(string Name, int? PointsLimit);

public sealed record AddEntryRequest(string DatasheetId, int? Quantity, IReadOnlyList<string>? SelectedOptionIds);

public sealed record UpdateEntryRequest(int Quantity, IReadOnlyList<string>? SelectedOptionIds);

public sealed record SelectedOptionDto(string Id, string Name, int Points);

public sealed record ListEntryDto(
    Guid Id,
    string DatasheetId,
    string DatasheetName,
    int Points,
    int OptionsPoints,
    int Quantity,
    int LineTotal,
    IReadOnlyList<SelectedOptionDto> SelectedOptions);

public sealed record ArmyListSummaryDto(
    Guid Id,
    string Name,
    string GameSystemKey,
    string FactionId,
    string FactionName,
    int? PointsLimit,
    int Total,
    int EntryCount,
    DateTimeOffset UpdatedAt);

public sealed record ArmyListDto(
    Guid Id,
    string Name,
    string GameSystemKey,
    string FactionId,
    string FactionName,
    int? PointsLimit,
    int Total,
    bool IsOverLimit,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ListEntryDto> Entries);
