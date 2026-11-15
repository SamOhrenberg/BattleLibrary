namespace BattleLibrary.Api.Lists;

// List-level fields. Everything except the name is optional; a blank name falls
// back to a default.
public sealed record ListFieldsRequest(
    string? Name,
    string? GameSystemKey,
    string? FactionName = null,
    string? Detachment = null,
    int? PointsLimit = null,
    string? Notes = null);

public sealed record SetFeaturedRequest(bool Featured);

public sealed record SetWarlordRequest(bool IsWarlord);

// Attach this entry to another entry; null detaches.
public sealed record AttachEntryRequest(Guid? AttachToEntryId);

// The fields of one unit. ForceId picks the section; null keeps the current one
// (or the primary section for a new entry).
public sealed record EntryFieldsRequest(
    string? Name,
    int? Points = null,
    int? ModelCount = null,
    string? Wargear = null,
    string? Enhancement = null,
    string? Notes = null,
    Guid? ForceId = null);

public sealed record ForceFieldsRequest(string? Name);

public sealed record ListForceDto(Guid Id, string Name, int SortOrder);

// The owner's full custom ordering of a list's entries, by entry id.
public sealed record SetEntryOrderRequest(IReadOnlyList<Guid> EntryIds);

public sealed record PresetFieldsRequest(
    string? GameSystemKey,
    string? Name,
    string? UnitName,
    int? Points = null,
    int? ModelCount = null,
    string? Wargear = null,
    string? Enhancement = null,
    string? Notes = null);

public sealed record LoadoutPresetDto(
    Guid Id,
    string GameSystemKey,
    string Name,
    string UnitName,
    int? Points,
    int? ModelCount,
    string? Wargear,
    string? Enhancement,
    string? Notes,
    DateTimeOffset UpdatedAt);

public sealed record ListEntryDto(
    Guid Id,
    Guid ForceId,
    string Name,
    int? Points,
    int? ModelCount,
    string? Wargear,
    string? Enhancement,
    string? Notes,
    int SortOrder,
    bool IsWarlord,
    Guid? AttachedToEntryId);

public sealed record ArmyListSummaryDto(
    Guid Id,
    string Name,
    string GameSystemKey,
    string FactionName,
    int? PointsLimit,
    int Total,
    int EntryCount,
    bool Featured,
    DateTimeOffset UpdatedAt);

public sealed record ArmyListDto(
    Guid Id,
    string Name,
    string GameSystemKey,
    string FactionName,
    string? Detachment,
    int? PointsLimit,
    string? Notes,
    int Total,
    bool Featured,
    IReadOnlyList<ListForceDto> Forces,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ListEntryDto> Entries);
