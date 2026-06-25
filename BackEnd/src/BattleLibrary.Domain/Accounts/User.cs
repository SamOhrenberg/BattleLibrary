using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Accounts;

public class User : Entity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public Profile Profile { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
