using BattleLibrary.Domain.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.Property(c => c.ClientName).IsRequired().HasMaxLength(200);
        builder.Ignore(c => c.PublicId);
    }
}

public sealed class OAuthGrantConfiguration : IEntityTypeConfiguration<OAuthGrant>
{
    public void Configure(EntityTypeBuilder<OAuthGrant> builder)
    {
        builder.Property(g => g.Scope).IsRequired().HasMaxLength(200);
        builder.Property(g => g.CodeHash).IsRequired();
        builder.Property(g => g.CodeChallenge).IsRequired().HasMaxLength(64);
        builder.Property(g => g.RedirectUri).IsRequired().HasMaxLength(2000);
        builder.Ignore(g => g.IsConnected);

        builder.HasIndex(g => g.CodeHash).IsUnique();
        builder.HasIndex(g => g.RefreshTokenHash).IsUnique();
        builder.HasIndex(g => g.UserId);

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(g => g.Client)
            .WithMany()
            .HasForeignKey(g => g.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
