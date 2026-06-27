using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BattleLibrary.Infrastructure.Persistence;

// Used only by `dotnet ef` at design time (migrations), never at runtime.
public sealed class BattleLibraryDbContextFactory : IDesignTimeDbContextFactory<BattleLibraryDbContext>
{
    public BattleLibraryDbContext CreateDbContext(string[] args)
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");
        var connectionString = !string.IsNullOrWhiteSpace(url)
            ? DatabaseConnectionString.FromUrl(url)
            : "Host=localhost;Port=5432;Database=battlelibrary;Username=battlelibrary;Password=battlelibrary";

        var options = new DbContextOptionsBuilder<BattleLibraryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BattleLibraryDbContext(options);
    }
}
