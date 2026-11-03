using BattleLibrary.Infrastructure.BsData.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class CachedGameSystemConfiguration : IEntityTypeConfiguration<CachedGameSystem>
{
    public void Configure(EntityTypeBuilder<CachedGameSystem> builder)
    {
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Repo).HasMaxLength(200);
        builder.Property(s => s.CommitSha).HasMaxLength(64);

        builder.HasMany(s => s.Factions)
            .WithOne(f => f.GameSystem)
            .HasForeignKey(f => f.GameSystemKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CachedFactionConfiguration : IEntityTypeConfiguration<CachedFaction>
{
    public void Configure(EntityTypeBuilder<CachedFaction> builder)
    {
        builder.Property(f => f.BsDataId).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.EquipmentJson).HasColumnType("jsonb");
        builder.HasIndex(f => f.GameSystemKey);
        builder.HasIndex(f => f.BsDataId);

        builder.HasMany(f => f.Datasheets)
            .WithOne(d => d.Faction)
            .HasForeignKey(d => d.FactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CachedDatasheetConfiguration : IEntityTypeConfiguration<CachedDatasheet>
{
    public void Configure(EntityTypeBuilder<CachedDatasheet> builder)
    {
        builder.Property(d => d.BsDataId).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.AvailabilityJson).HasColumnType("jsonb");
        builder.Property(d => d.ModelJson).HasColumnType("jsonb");
        builder.HasIndex(d => d.FactionId);
        builder.HasIndex(d => d.BsDataId);
    }
}
