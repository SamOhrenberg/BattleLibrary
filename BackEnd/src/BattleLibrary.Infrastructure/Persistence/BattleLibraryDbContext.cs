using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Infrastructure.Persistence;

/// <summary>
/// Application EF Core database context.
/// </summary>
public class BattleLibraryDbContext(DbContextOptions<BattleLibraryDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BattleLibraryDbContext).Assembly);
    }
}
