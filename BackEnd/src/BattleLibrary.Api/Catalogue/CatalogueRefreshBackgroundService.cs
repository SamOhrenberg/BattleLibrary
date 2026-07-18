using BattleLibrary.Infrastructure.BsData;

namespace BattleLibrary.Api.Catalogue;

// Polls the source repo on an interval and refreshes the cache when the branch
// has a new commit. RefreshAsync(force: false) is cheap when nothing changed:
// one commit lookup, then a download + rebuild only on a new SHA.
public sealed class CatalogueRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    BsDataOptions options,
    ILogger<CatalogueRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.AutoRefreshEnabled)
        {
            logger.LogInformation("Catalogue auto-refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.PollIntervalMinutes));
        logger.LogInformation("Catalogue auto-refresh enabled; polling every {Minutes} minutes.", interval.TotalMinutes);

        await RefreshOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshOnceAsync(stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<BsDataRefreshService>();
            foreach (var result in await service.RefreshAllAsync(force: false, ct))
            {
                if (result.Changed)
                {
                    logger.LogInformation(
                        "Catalogue {System} refreshed from {Sha}: {Factions} factions, {Datasheets} datasheets.",
                        result.SystemKey, result.CommitSha, result.Factions, result.Datasheets);
                }
                else
                {
                    logger.LogDebug("Catalogue {System} already current ({Sha}).", result.SystemKey, result.CommitSha);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalogue auto-refresh cycle failed; retrying next interval.");
        }
    }
}
