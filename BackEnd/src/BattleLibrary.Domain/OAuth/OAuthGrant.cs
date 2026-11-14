using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.OAuth;

// One user's authorization of one client. Starts as a one-time code; exchanging
// it sets the refresh token, which rotates on every refresh.
public class OAuthGrant : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid ClientId { get; set; }
    public OAuthClient Client { get; set; } = null!;

    public string Scope { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public DateTimeOffset CodeExpiresAt { get; set; }
    public DateTimeOffset? CodeUsedAt { get; set; }

    public string? RefreshTokenHash { get; set; }
    public DateTimeOffset? RefreshExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsConnected => RevokedAt is null && CodeUsedAt is not null;
}
