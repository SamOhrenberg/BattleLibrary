using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Accounts;

public class User : Entity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // Site administrator: can toggle open registration and manage invites.
    public bool IsAdmin { get; set; }

    public Profile Profile { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
