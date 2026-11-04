using BattleLibrary.Infrastructure.BsData.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class CatalogueSystemSettingConfiguration : IEntityTypeConfiguration<CatalogueSystemSetting>
{
    public void Configure(EntityTypeBuilder<CatalogueSystemSetting> builder)
    {
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
    }
}
