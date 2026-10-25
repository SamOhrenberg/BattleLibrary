using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Lists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class ArmyListConfiguration : IEntityTypeConfiguration<ArmyList>
{
    public void Configure(EntityTypeBuilder<ArmyList> builder)
    {
        builder.Property(l => l.Name).IsRequired().HasMaxLength(120);
        builder.Property(l => l.GameSystemKey).IsRequired().HasMaxLength(100);
        builder.Property(l => l.FactionId).IsRequired().HasMaxLength(100);
        builder.Property(l => l.FactionName).HasMaxLength(200);
        builder.Property(l => l.BattleSize).HasMaxLength(50);
        builder.OwnsMany(l => l.Detachments, d => d.ToJson());
        builder.HasIndex(l => l.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Entries)
            .WithOne(e => e.ArmyList)
            .HasForeignKey(e => e.ArmyListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ListEntryConfiguration : IEntityTypeConfiguration<ListEntry>
{
    public void Configure(EntityTypeBuilder<ListEntry> builder)
    {
        builder.Property(e => e.DatasheetId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DatasheetName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EnhancementId).HasMaxLength(100);
        builder.Property(e => e.EnhancementName).HasMaxLength(200);
        builder.OwnsMany(e => e.SelectedOptions, o => o.ToJson());
        builder.HasIndex(e => e.ArmyListId);
    }
}
