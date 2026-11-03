namespace BattleLibrary.Api.Lists;

public sealed record CreateListRequest(string Name, string GameSystemKey, string FactionId, int? PointsLimit, string? BattleSize = null);

public sealed record UpdateListRequest(string Name, int? PointsLimit);

public sealed record SetFeaturedRequest(bool Featured);

public sealed record SetBattleSizeRequest(string? BattleSize);

public sealed record SetDispositionRequest(string? ForceDisposition);

public sealed record SetDetachmentsRequest(IReadOnlyList<string> DetachmentIds);

public sealed record ListDetachmentDto(string Id, string Name, int Dp);

public sealed record SetWarlordRequest(bool IsWarlord);

// Attach a Leader (this entry) to a Bodyguard entry; null detaches.
public sealed record AttachEntryRequest(Guid? AttachToEntryId);

public sealed record SetEnhancementRequest(string? EnhancementId);

// A per-model loadout variant and how many of it the pool holds.
public sealed record VariantQuantityDto(string Id, int Quantity);

// ForceId picks which force the unit joins; null uses the primary force.
public sealed record AddEntryRequest(
    string DatasheetId,
    IReadOnlyList<string>? SelectedOptionIds,
    int? Models = null,
    Guid? ForceId = null,
    IReadOnlyList<VariantQuantityDto>? Variants = null);

// Add an allied force (a faction) to the list.
public sealed record AddForceRequest(string FactionId);

public sealed record ListForceDto(Guid Id, string FactionId, string FactionName, int SortOrder);

// Each field is optional: a null field is left unchanged, so option edits and
// name/notes edits can be sent independently. For CustomName/Notes an empty or
// whitespace string clears the value.
public sealed record UpdateEntryRequest(
    IReadOnlyList<string>? SelectedOptionIds,
    string? CustomName = null,
    string? Notes = null,
    IReadOnlyList<VariantQuantityDto>? Variants = null);

public sealed record SetModelCountRequest(int Models, IReadOnlyList<VariantQuantityDto>? Variants = null);

// The owner's full custom ordering of a list's entries, by entry id.
public sealed record SetEntryOrderRequest(IReadOnlyList<Guid> EntryIds);

public sealed record SelectedOptionDto(string Id, string Name, int Points, int Quantity);

// A saved loadout for one datasheet, reusable across the owner's lists.
public sealed record CreatePresetRequest(
    string GameSystemKey,
    string DatasheetId,
    string Name,
    int? Models,
    IReadOnlyList<string>? SelectedOptionIds,
    IReadOnlyList<VariantQuantityDto>? Variants = null);

public sealed record LoadoutPresetDto(
    Guid Id,
    string GameSystemKey,
    string DatasheetId,
    string DatasheetName,
    string Name,
    int ModelCount,
    int Points,
    int Total,
    IReadOnlyList<SelectedOptionDto> SelectedOptions,
    DateTimeOffset UpdatedAt);

public sealed record ListEntryDto(
    Guid Id,
    Guid ForceId,
    string DatasheetId,
    string DatasheetName,
    string? CustomName,
    string? Notes,
    int Points,
    int OptionsPoints,
    int ModelCount,
    int SortOrder,
    int LineTotal,
    IReadOnlyList<string> Keywords,
    bool IsWarlord,
    bool ForcedWarlord,
    Guid? AttachedToEntryId,
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

// Result of importing a BattleScribe roster: the created list plus the names of
// any selections that couldn't be matched to a cached datasheet/option.
public sealed record ImportRosterResultDto(ArmyListDto List, IReadOnlyList<string> Unmatched);

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
    string? ForceDisposition,
    bool UsesLegends,
    bool UsesCrucible,
    IReadOnlyList<ListDetachmentDto> Detachments,
    IReadOnlyList<ListForceDto> Forces,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ListEntryDto> Entries);
