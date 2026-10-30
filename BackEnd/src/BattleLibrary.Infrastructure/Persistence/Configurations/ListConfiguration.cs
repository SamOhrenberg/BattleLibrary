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
        builder.Property(l => l.ForceDisposition).HasMaxLength(100);
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

        builder.HasMany(l => l.Forces)
            .WithOne(f => f.ArmyList)
            .HasForeignKey(f => f.ArmyListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ListForceConfiguration : IEntityTypeConfiguration<ListForce>
{
    public void Configure(EntityTypeBuilder<ListForce> builder)
    {
        builder.Property(f => f.FactionId).IsRequired().HasMaxLength(100);
        builder.Property(f => f.FactionName).HasMaxLength(200);
        builder.HasIndex(f => f.ArmyListId);
    }
}

public sealed class ListEntryConfiguration : IEntityTypeConfiguration<ListEntry>
{
    public void Configure(EntityTypeBuilder<ListEntry> builder)
    {
        builder.Property(e => e.DatasheetId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DatasheetName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.CustomName).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.EnhancementId).HasMaxLength(100);
        builder.Property(e => e.EnhancementName).HasMaxLength(200);
        builder.OwnsMany(e => e.SelectedOptions, o => o.ToJson());
        builder.HasIndex(e => e.ArmyListId);
        builder.HasIndex(e => e.ListForceId);
    }
}

public sealed class LoadoutPresetConfiguration : IEntityTypeConfiguration<LoadoutPreset>
{
    public void Configure(EntityTypeBuilder<LoadoutPreset> builder)
    {
        builder.Property(p => p.GameSystemKey).IsRequired().HasMaxLength(100);
        builder.Property(p => p.DatasheetId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.DatasheetName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(80);
        builder.OwnsMany(p => p.SelectedOptions, o => o.ToJson());
        builder.HasIndex(p => new { p.UserId, p.GameSystemKey, p.DatasheetId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
