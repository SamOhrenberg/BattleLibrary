namespace BattleLibrary.Api.Auth;

public sealed record RegisterRequest(string Username, string Email, string Password);

public sealed record LoginRequest(string UsernameOrEmail, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserDto(Guid Id, string Username, string Email, string Visibility);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
