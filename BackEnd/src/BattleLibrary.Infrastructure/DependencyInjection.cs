using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BattleLibrary.Infrastructure;

/// <summary>
/// Registers infrastructure services (EF Core, DbContext).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration);

        services.AddDbContext<BattleLibraryDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
