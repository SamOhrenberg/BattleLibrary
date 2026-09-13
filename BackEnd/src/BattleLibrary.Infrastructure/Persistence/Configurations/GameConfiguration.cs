using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Games;
using BattleLibrary.Domain.Lists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.Property(g => g.Result).HasConversion<int>();
        builder.Property(g => g.GameSystemKey).HasMaxLength(100);
        builder.Property(g => g.PlayerFactionName).HasMaxLength(200);
        builder.Property(g => g.OpponentName).IsRequired().HasMaxLength(80);
        builder.Property(g => g.OpponentFactionName).HasMaxLength(200);
        builder.Property(g => g.Notes).HasMaxLength(1000);

        builder.HasIndex(g => new { g.UserId, g.PlayedOn });

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Opponent user and player list are soft links: drop the reference if the
        // target is deleted, but keep the game and its snapshotted names.
        builder.HasOne(g => g.OpponentUser)
            .WithMany()
            .HasForeignKey(g => g.OpponentUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(g => g.PlayerList)
            .WithMany()
            .HasForeignKey(g => g.PlayerListId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
