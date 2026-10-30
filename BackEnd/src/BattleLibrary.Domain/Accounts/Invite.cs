using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Accounts;

// An invitation to register while open sign-ups are off. Either tied to a
// specific Email, or a shareable link (Email null). Single-use by default.
public class Invite : Entity
{
    // Opaque, URL-safe token the invitee presents when registering.
    public string Token { get; set; } = string.Empty;

    // When set, only this email may register with the invite. Null = open link.
    public string? Email { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    // Allowed registrations; 0 means unlimited.
    public int MaxUses { get; set; } = 1;
    public int UseCount { get; set; }

    public bool Revoked { get; set; }

    // The admin who created it.
    public Guid CreatedByUserId { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        !Revoked
        && (ExpiresAt is null || ExpiresAt > now)
        && (MaxUses == 0 || UseCount < MaxUses);
}
