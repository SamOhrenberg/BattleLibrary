using System.Text.Json;
using System.Xml.Linq;
using BattleLibrary.Infrastructure.BsData.Caching;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Infrastructure.BsData;

public sealed record RefreshResult(
    string SystemKey, bool Changed, string CommitSha, int Factions, int Datasheets, string Message);

file static class CacheJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed class BsDataRefreshService(
    BattleLibraryDbContext db,
    BsDataGitHubClient gitHub,
    BsDataParser parser)
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

    public Task<RefreshResult> RefreshAsync(string systemKey, bool force, CancellationToken ct)
    {
        var source = BsDataSource.All.FirstOrDefault(s => s.SystemKey == systemKey)
                     ?? throw new InvalidOperationException($"Unknown game system '{systemKey}'.");
        return RefreshAsync(source, force, ct);
    }

    public async Task<RefreshResult> RefreshAsync(BsDataSource source, bool force, CancellationToken ct)
    {
        var sha = await gitHub.GetCommitShaAsync(source.Repo, source.Branch, ct);

        var existing = await db.CachedGameSystems.FirstOrDefaultAsync(s => s.Key == source.SystemKey, ct);
        if (!force && existing is not null && existing.CommitSha == sha)
        {
            return new RefreshResult(source.SystemKey, false, sha, 0, 0, "Already up to date.");
        }

        var (systemText, factionTexts, libraryTexts) = await DownloadAsync(source, sha, ct);
        var models = source.Format == SourceFormat.Xml
            ? parser.ParseAllXml(systemText, factionTexts, libraryTexts, source.Schema)
            : parser.ParseAll(systemText, factionTexts, libraryTexts, source.Schema);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.CachedFactions
            .Where(f => f.GameSystemKey == source.SystemKey)
            .ExecuteDeleteAsync(ct);

        var system = existing ?? new CachedGameSystem { Key = source.SystemKey, Enabled = true };
        system.Name = source.DisplayName;
        system.Repo = source.Repo;
        system.CommitSha = sha;
        system.RefreshedAt = DateTimeOffset.UtcNow;
        // Roster-header identity is the same across every catalogue of a system.
        var systemInfo = models.FirstOrDefault()?.GameSystem;
        if (systemInfo is not null)
        {
            system.BsDataId = systemInfo.Id;
            system.BsDataName = systemInfo.Name;
            system.Revision = systemInfo.Revision;
            system.PointsCostTypeId = systemInfo.PointsCostTypeId;
            system.ForceEntryId = systemInfo.ForceEntryId;
            system.ForceEntryName = systemInfo.ForceEntryName;
            system.DispositionsJson = JsonSerializer.Serialize(systemInfo.Dispositions, CacheJson.Options);
        }
        if (existing is null)
        {
            db.CachedGameSystems.Add(system);
        }

        var datasheetTotal = 0;
        foreach (var model in models)
        {
            // Skip empty factions (e.g. shared-library catalogues with no units).
            if (model.Datasheets.Count == 0)
            {
                continue;
            }
            var faction = new CachedFaction
            {
                GameSystemKey = source.SystemKey,
                BsDataId = model.FactionId,
                Name = model.FactionName,
                DatasheetCount = model.Datasheets.Count,
                Revision = model.FactionRevision,
                EquipmentJson = JsonSerializer.Serialize(model.Equipment, CacheJson.Options),
                DetachmentsJson = JsonSerializer.Serialize(model.Detachments, CacheJson.Options),
            };
            foreach (var sheet in model.Datasheets)
            {
                faction.Datasheets.Add(new CachedDatasheet
                {
                    BsDataId = sheet.Id,
                    Name = sheet.Name,
                    Points = sheet.Points,
                    SortIndex = sheet.SortIndex,
                    KeywordsCsv = string.Join('|', sheet.Keywords),
                    AvailabilityJson = JsonSerializer.Serialize(sheet.Availability, CacheJson.Options),
                    ModelJson = JsonSerializer.Serialize(sheet, CacheJson.Options),
                });
            }
            datasheetTotal += model.Datasheets.Count;
            db.CachedFactions.Add(faction);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new RefreshResult(source.SystemKey, true, sha, models.Count(m => m.Datasheets.Count > 0), datasheetTotal, "Refreshed.");
    }

    public Task PurgeAsync(string key, CancellationToken ct) =>
        db.CachedGameSystems.Where(s => s.Key == key).ExecuteDeleteAsync(ct);

    public Task SetEnabledAsync(string key, bool enabled, CancellationToken ct) =>
        db.CachedGameSystems
            .Where(s => s.Key == key)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Enabled, enabled), ct);

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

        foreach (var path in await gitHub.ListTopLevelFilesAsync(source.Repo, sha, [".json"], ct))
        {
            var json = await gitHub.DownloadRawAsync(source.Repo, sha, path, ct);
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

        var files = await gitHub.ListTopLevelFilesAsync(source.Repo, sha, [".cat", ".gst"], ct);
        foreach (var path in files.Where(p => p.StartsWith(source.FilePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var xml = await gitHub.DownloadRawAsync(source.Repo, sha, path, ct);
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
