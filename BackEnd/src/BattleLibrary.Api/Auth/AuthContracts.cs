namespace BattleLibrary.Api.Auth;

public sealed record RegisterRequest(string Username, string Email, string Password, string? InviteToken = null);

public sealed record LoginRequest(string UsernameOrEmail, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserDto(Guid Id, string Username, string Email, string Visibility, bool IsAdmin);

// Whether open sign-ups are on, so the register screen can message correctly.
public sealed record RegistrationStatusDto(bool Open);

// Admin: site settings + invite management.
public sealed record AdminSettingsDto(bool RegistrationOpen);
public sealed record UpdateSettingsRequest(bool RegistrationOpen);
public sealed record CreateInviteRequest(string? Email, int? MaxUses, int? ExpiresInDays);
public sealed record InviteDto(
    Guid Id, string Token, string? Email, DateTimeOffset? ExpiresAt,
    int MaxUses, int UseCount, bool Revoked, bool Usable, DateTimeOffset CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
