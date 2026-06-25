using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Accounts;

public class Profile : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ProfileVisibility Visibility { get; set; } = ProfileVisibility.Private;
    public string? Bio { get; set; }
}
