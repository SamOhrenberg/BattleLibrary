namespace BattleLibrary.Api.Lists;

public sealed record CreateListRequest(string Name, string GameSystemKey, string FactionId, int? PointsLimit, string? BattleSize = null);

public sealed record UpdateListRequest(string Name, int? PointsLimit);

public sealed record SetFeaturedRequest(bool Featured);

public sealed record SetBattleSizeRequest(string? BattleSize);

public sealed record SetDetachmentsRequest(IReadOnlyList<string> DetachmentIds);

public sealed record ListDetachmentDto(string Id, string Name, int Dp);

public sealed record SetWarlordRequest(bool IsWarlord);

public sealed record SetEnhancementRequest(string? EnhancementId);

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
    IReadOnlyList<string> Keywords,
    bool IsWarlord,
    string? EnhancementId,
    string? EnhancementName,
    int EnhancementPoints,
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
    bool Featured,
    string? BattleSize,
    bool UsesLegends,
    bool UsesCrucible,
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
    bool Featured,
    string? BattleSize,
    bool UsesLegends,
    bool UsesCrucible,
    IReadOnlyList<ListDetachmentDto> Detachments,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ListEntryDto> Entries);
