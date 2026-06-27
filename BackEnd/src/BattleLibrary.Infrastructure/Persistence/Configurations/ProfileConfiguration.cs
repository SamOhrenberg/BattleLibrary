using BattleLibrary.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.Property(p => p.Visibility).HasConversion<int>();
        builder.Property(p => p.Bio).HasMaxLength(500);
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}
