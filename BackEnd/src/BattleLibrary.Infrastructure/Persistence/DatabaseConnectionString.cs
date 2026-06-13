using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BattleLibrary.Infrastructure.Persistence;

/// <summary>
/// Resolves the PostgreSQL connection string from configuration.
/// </summary>
public static class DatabaseConnectionString
{
    public static string Resolve(IConfiguration configuration)
    {
        // Explicit key/value connection string (local dev, user-secrets).
        var fromConfig = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        // URL-style connection string from a managed host (Railway, Heroku).
        var url = configuration["DATABASE_URL"]
                  ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            return FromUrl(url);
        }

        throw new InvalidOperationException(
            "No database connection configured. Set ConnectionStrings:Default " +
            "(local development) or DATABASE_URL (deployment).");
    }

    /// <summary>Converts a postgres:// URL into an Npgsql connection string.</summary>
    public static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = uri.AbsolutePath.TrimStart('/'),
            // Use SSL when the host offers it; skip on a plain local connection.
            SslMode = SslMode.Prefer,
        };

        return builder.ConnectionString;
    }
}
