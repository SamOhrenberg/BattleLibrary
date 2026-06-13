namespace BattleLibrary.Domain.Common;

/// <summary>
/// Base class for persistent entities.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
