namespace BattleLibrary.Domain.Accounts;

// Single-row site configuration the admin controls at runtime.
public class AppSetting
{
    // Fixed id: there is exactly one settings row.
    public int Id { get; set; } = 1;

    // When false, only the seed admin, the first user, or a valid invite can
    // register. New lists stay invite-only until an admin flips this on.
    public bool RegistrationOpen { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
