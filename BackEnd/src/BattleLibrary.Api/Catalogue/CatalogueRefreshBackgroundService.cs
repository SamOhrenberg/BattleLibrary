using System.Runtime;
using BattleLibrary.Infrastructure.BsData;
using BattleLibrary.Infrastructure.BsData.Caching;

namespace BattleLibrary.Api.Catalogue;

// Loads every catalogue into memory at startup (retrying any source that
// fails), then optionally polls for new commits. RefreshAsync(force: false) is
// cheap when nothing changed: one commit lookup, then a download + rebuild only
// on a new SHA.
public sealed class CatalogueRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    CatalogueStore store,
    BsDataOptions options,
    ILogger<CatalogueRefreshBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.LoadOnStartup)
        {
            logger.LogWarning("Catalogue startup load is disabled; the catalogue is empty.");
            store.MarkReady();
            return;
        }

        // Readers stop answering 503 once every source has been tried; a source
        // that failed is absent until a retry below loads it.
        var allLoaded = await RefreshOnceAsync(stoppingToken);
        store.MarkReady();

        var retryDelay = TimeSpan.FromSeconds(15);
        while (!allLoaded)
        {
            logger.LogWarning("Catalogue startup load incomplete; retrying in {Seconds}s.", retryDelay.TotalSeconds);
            await Task.Delay(retryDelay, stoppingToken);
            retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            allLoaded = await RefreshOnceAsync(stoppingToken);
        }

        if (!options.AutoRefreshEnabled)
        {
            logger.LogInformation("Catalogue auto-refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.PollIntervalMinutes));
        logger.LogInformation("Catalogue auto-refresh enabled; polling every {Minutes} minutes.", interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshOnceAsync(stoppingToken);
        }
    }

    // True when every source refreshed (or was already current or disabled).
    // Sources refresh independently, so one failing doesn't hold back the rest.
    private async Task<bool> RefreshOnceAsync(CancellationToken ct)
    {
        var allOk = true;
        var anyChanged = false;
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BsDataRefreshService>();
        foreach (var source in BsDataSource.All)
        {
            try
            {
                var result = await service.RefreshAsync(source, force: false, ct);
                if (result.Changed)
                {
                    anyChanged = true;
                    logger.LogInformation(
                        "Catalogue {System} loaded from {Sha}: {Factions} factions, {Datasheets} datasheets.",
                        result.SystemKey, result.CommitSha, result.Factions, result.Datasheets);
                }
                else
                {
                    logger.LogDebug("Catalogue {System}: {Message} ({Sha}).", result.SystemKey, result.Message, result.CommitSha);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host is shutting down.
                return true;
            }
            catch (Exception ex)
            {
                allOk = false;
                logger.LogError(ex, "Catalogue {System} refresh failed; retrying.", source.SystemKey);
            }
        }

        if (anyChanged)
        {
            ReleaseParseGarbage();
        }
        return allOk;
    }

    // A rebuild leaves the raw repo files and parse trees as garbage (hundreds of
    // MB); compact once so the process returns that memory instead of holding it.
    private void ReleaseParseGarbage()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        logger.LogInformation(
            "Catalogue in memory; managed heap {HeapMb} MB after compaction.",
            GC.GetGCMemoryInfo().HeapSizeBytes / (1024 * 1024));
    }
}
