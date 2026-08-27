namespace BattleLibrary.Api.Social;

public sealed record UpdateProfileRequest(string Visibility, string? Bio);

public sealed record ProfileSettingsDto(string Visibility, string? Bio);

public sealed record SendFriendRequestBody(string Username);

public sealed record FriendDto(Guid Id, string Username, string Visibility);

// Direction is "incoming" (they asked you) or "outgoing" (you asked them).
public sealed record FriendRequestDto(Guid Id, Guid UserId, string Username, string Direction);

public sealed record FriendRequestsDto(
    IReadOnlyList<FriendRequestDto> Incoming,
    IReadOnlyList<FriendRequestDto> Outgoing);

// FriendshipStatus: self | none | pending_outgoing | pending_incoming | friends.
public sealed record UserSearchResultDto(Guid Id, string Username, string Visibility, string FriendshipStatus);

public sealed record PublicListDto(
    Guid Id,
    string Name,
    string GameSystemKey,
    string FactionName,
    int? PointsLimit,
    int Total,
    int EntryCount,
    DateTimeOffset UpdatedAt);

public sealed record PublicProfileDto(
    Guid Id,
    string Username,
    string Visibility,
    string? Bio,
    string FriendshipStatus,
    bool CanViewLists,
    IReadOnlyList<PublicListDto> Lists);
