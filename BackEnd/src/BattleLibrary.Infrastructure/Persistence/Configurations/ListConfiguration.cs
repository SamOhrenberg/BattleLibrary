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
        builder.Property(l => l.FactionName).HasMaxLength(200);
        builder.Property(l => l.Detachment).HasMaxLength(200);
        builder.Property(l => l.Notes).HasMaxLength(4000);
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
        builder.Property(f => f.Name).HasMaxLength(200);
        builder.HasIndex(f => f.ArmyListId);
    }
}

public sealed class ListEntryConfiguration : IEntityTypeConfiguration<ListEntry>
{
    public void Configure(EntityTypeBuilder<ListEntry> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Wargear).HasMaxLength(2000);
        builder.Property(e => e.Enhancement).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.HasIndex(e => e.ArmyListId);
        builder.HasIndex(e => e.ListForceId);
    }
}

public sealed class LoadoutPresetConfiguration : IEntityTypeConfiguration<LoadoutPreset>
{
    public void Configure(EntityTypeBuilder<LoadoutPreset> builder)
    {
        builder.Property(p => p.GameSystemKey).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(80);
        builder.Property(p => p.UnitName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Wargear).HasMaxLength(2000);
        builder.Property(p => p.Enhancement).HasMaxLength(200);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.HasIndex(p => new { p.UserId, p.GameSystemKey });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
