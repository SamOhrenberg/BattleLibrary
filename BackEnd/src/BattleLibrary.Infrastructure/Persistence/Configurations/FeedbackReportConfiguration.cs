using BattleLibrary.Domain.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BattleLibrary.Infrastructure.Persistence.Configurations;

public sealed class FeedbackReportConfiguration : IEntityTypeConfiguration<FeedbackReport>
{
    public void Configure(EntityTypeBuilder<FeedbackReport> builder)
    {
        builder.Property(f => f.Kind).HasConversion<int>();
        builder.Property(f => f.Title).IsRequired().HasMaxLength(120);
        builder.Property(f => f.Area).HasMaxLength(64);
        builder.Property(f => f.IssueUrl).IsRequired().HasMaxLength(256);
        builder.Property(f => f.State).IsRequired().HasMaxLength(16);
        builder.Property(f => f.StateReason).HasMaxLength(32);

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.IssueNumber).IsUnique();

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
