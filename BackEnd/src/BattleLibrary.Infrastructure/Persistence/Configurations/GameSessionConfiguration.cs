using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    public void Configure(EntityTypeBuilder<GameSession> builder)
    {
        builder.Property(s => s.Status).HasConversion<int>();
        builder.Property(s => s.Name).HasMaxLength(120);
        builder.Property(s => s.GameSystemKey).HasMaxLength(100);
        builder.HasIndex(s => s.CreatedByUserId);

        builder.HasMany(s => s.Participants)
            .WithOne(p => p.GameSession)
            .HasForeignKey(p => p.GameSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GameParticipantConfiguration : IEntityTypeConfiguration<GameParticipant>
{
    public void Configure(EntityTypeBuilder<GameParticipant> builder)
    {
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.FactionName).HasMaxLength(200);

        // One seat per user per session.
        builder.HasIndex(p => new { p.GameSessionId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-player play state lives as JSON on the row; nested collections keep
        // their own stable ids so the API can target a single unit or action.
        builder.OwnsMany(p => p.Units, u => u.ToJson());
        builder.OwnsMany(p => p.Actions, a => a.ToJson());
        builder.OwnsMany(p => p.Rounds, r =>
        {
            r.ToJson();
            r.OwnsMany(x => x.Secondaries);
        });
    }
}
