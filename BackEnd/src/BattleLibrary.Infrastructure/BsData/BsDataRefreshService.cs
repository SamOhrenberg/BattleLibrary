using System.Text.Json;
using System.Xml.Linq;
using BattleLibrary.Infrastructure.BsData.Caching;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Infrastructure.BsData;

public sealed record RefreshResult(
    string SystemKey, bool Changed, string CommitSha, int Factions, int Datasheets, string Message);

public sealed class BsDataRefreshService(
    BattleLibraryDbContext db,
    BsDataGitHubClient gitHub,
    BsDataParser parser,
    CatalogueStore store)
{
    public async Task<IReadOnlyList<RefreshResult>> RefreshAllAsync(bool force, CancellationToken ct)
    {
        var results = new List<RefreshResult>();
        foreach (var source in BsDataSource.All)
        {
            results.Add(await RefreshAsync(source, force, ct));
        }
        return results;
    }

    public Task<RefreshResult> RefreshAsync(string systemKey, bool force, CancellationToken ct) =>
        RefreshAsync(FindSource(systemKey), force, ct);

    // Downloads and parses the source into a fresh in-memory snapshot. Nothing
    // BSData-derived is written to the database.
    public async Task<RefreshResult> RefreshAsync(BsDataSource source, bool force, CancellationToken ct)
    {
        await store.RefreshLock.WaitAsync(ct);
        try
        {
            if (!await IsEnabledAsync(source.SystemKey, ct))
            {
                store.Remove(source.SystemKey);
                return new RefreshResult(source.SystemKey, false, "", 0, 0, "Disabled.");
            }

            var sha = await gitHub.GetCommitShaAsync(source.Repo, source.Branch, ct);
            if (!force && store.Get(source.SystemKey)?.CommitSha == sha)
            {
                return new RefreshResult(source.SystemKey, false, sha, 0, 0, "Already up to date.");
            }

            var (systemText, factionTexts, libraryTexts) = await DownloadAsync(source, sha, ct);
            var models = source.Format == SourceFormat.Xml
                ? parser.ParseAllXml(systemText, factionTexts, libraryTexts, source.Schema)
                : parser.ParseAll(systemText, factionTexts, libraryTexts, source.Schema);

            var snapshot = CatalogueSnapshot.Build(source, sha, models, DateTimeOffset.UtcNow);
            store.Set(snapshot);
            return new RefreshResult(source.SystemKey, true, sha, snapshot.Factions.Count, snapshot.DatasheetCount, "Refreshed.");
        }
        finally
        {
            store.RefreshLock.Release();
        }
    }

    // Drops the loaded copy; the next refresh (or poll) loads it again. Use the
    // kill switch to keep it out.
    public Task PurgeAsync(string key, CancellationToken ct)
    {
        store.Remove(FindSource(key).SystemKey);
        return Task.CompletedTask;
    }

    public async Task SetEnabledAsync(string key, bool enabled, CancellationToken ct)
    {
        var source = FindSource(key);
        var setting = await db.CatalogueSystemSettings.FirstOrDefaultAsync(s => s.Key == source.SystemKey, ct);
        if (setting is null)
        {
            setting = new CatalogueSystemSetting { Key = source.SystemKey };
            db.CatalogueSystemSettings.Add(setting);
        }
        setting.Enabled = enabled;
        await db.SaveChangesAsync(ct);

        if (enabled)
        {
            await RefreshAsync(source, force: false, ct);
        }
        else
        {
            store.Remove(source.SystemKey);
        }
    }

    private Task<bool> IsEnabledAsync(string key, CancellationToken ct) =>
        db.CatalogueSystemSettings.AllAsync(s => s.Key != key || s.Enabled, ct);

    private static BsDataSource FindSource(string systemKey) =>
        BsDataSource.All.FirstOrDefault(s => s.SystemKey == systemKey)
        ?? throw new InvalidOperationException($"Unknown game system '{systemKey}'.");

    // Raw files download a few at a time; results keep the tree's order.
    private async Task<string[]> DownloadAllAsync(string repo, string sha, IReadOnlyList<string> paths, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallelDownloads);
        return await Task.WhenAll(paths.Select(async path =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await gitHub.DownloadRawAsync(repo, sha, path, ct);
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    private const int MaxParallelDownloads = 6;

    private async Task<(string System, List<string> Factions, List<string> Libraries)> DownloadAsync(
        BsDataSource source, string sha, CancellationToken ct) =>
        source.Format == SourceFormat.Xml
            ? await DownloadXmlAsync(source, sha, ct)
            : await DownloadJsonAsync(source, sha, ct);

    private async Task<(string, List<string>, List<string>)> DownloadJsonAsync(
        BsDataSource source, string sha, CancellationToken ct)
    {
        string? systemJson = null;
        var factions = new List<string>();
        var libraries = new List<string>();

        var paths = await gitHub.ListTopLevelFilesAsync(source.Repo, sha, [".json"], ct);
        foreach (var json in await DownloadAllAsync(source.Repo, sha, paths, ct))
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("gameSystem", out _))
            {
                systemJson = json;
            }
            else if (root.TryGetProperty("catalogue", out var catalogue))
            {
                var isLibrary = catalogue.TryGetProperty("library", out var lib) && lib.ValueKind == JsonValueKind.True;
                (isLibrary ? libraries : factions).Add(json);
            }
        }

        if (systemJson is null)
        {
            throw new InvalidOperationException($"No gameSystem file found in {source.Repo}.");
        }
        return (systemJson, factions, libraries);
    }

    private async Task<(string, List<string>, List<string>)> DownloadXmlAsync(
        BsDataSource source, string sha, CancellationToken ct)
    {
        string? systemXml = null;
        var factions = new List<string>();
        var libraries = new List<string>();

        var files = (await gitHub.ListTopLevelFilesAsync(source.Repo, sha, [".cat", ".gst"], ct))
            .Where(p => p.StartsWith(source.FilePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var texts = await DownloadAllAsync(source.Repo, sha, files, ct);
        foreach (var (path, xml) in files.Zip(texts))
        {
            if (path.EndsWith(".gst", StringComparison.OrdinalIgnoreCase))
            {
                systemXml = xml;
            }
            else
            {
                var isLibrary = string.Equals(
                    XDocument.Parse(xml).Root?.Attribute("library")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                (isLibrary ? libraries : factions).Add(xml);
            }
        }

        if (systemXml is null)
        {
            throw new InvalidOperationException($"No .gst game-system file found in {source.Repo} for prefix '{source.FilePrefix}'.");
        }
        return (systemXml, factions, libraries);
    }
}
