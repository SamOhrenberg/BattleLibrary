using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Social;

// A directed friend relationship: Requester asked Addressee. One row per pair;
// Accepted means both are friends regardless of direction.
public class Friendship : Entity
{
    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    public Guid AddresseeId { get; set; }
    public User Addressee { get; set; } = null!;

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTimeOffset? RespondedAt { get; set; }
}
