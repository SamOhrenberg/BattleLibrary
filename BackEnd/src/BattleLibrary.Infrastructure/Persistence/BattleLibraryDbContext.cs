using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;
using BattleLibrary.Domain.Games;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Domain.Social;
using BattleLibrary.Infrastructure.BsData.Caching;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Infrastructure.Persistence;

/// <summary>
/// Application EF Core database context.
/// </summary>
public class BattleLibraryDbContext(DbContextOptions<BattleLibraryDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<CatalogueSystemSetting> CatalogueSystemSettings => Set<CatalogueSystemSetting>();

    public DbSet<ArmyList> ArmyLists => Set<ArmyList>();
    public DbSet<ListForce> ListForces => Set<ListForce>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<LoadoutPreset> LoadoutPresets => Set<LoadoutPreset>();

    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameParticipant> GameParticipants => Set<GameParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BattleLibraryDbContext).Assembly);

        // Entity keys are assigned in code (UUIDv7), not by the store, so a child
        // added to an already-tracked parent is detected as an insert, not update.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }
        }
    }
}
