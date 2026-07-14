using System.Text.Json;

namespace BattleLibrary.Infrastructure.BsData;

// Reads a BSData repo from GitHub. API calls (commit, tree) hit api.github.com;
// file contents come from raw.githubusercontent.com pinned to a commit SHA.
public sealed class BsDataGitHubClient(HttpClient http)
{
    public async Task<string> GetCommitShaAsync(string repo, string branch, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/commits/{branch}";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return doc.RootElement.GetProperty("sha").GetString()
               ?? throw new InvalidOperationException("Commit response had no sha.");
    }

    // Top-level files (no '/') whose name ends with one of the given extensions.
    public async Task<IReadOnlyList<string>> ListTopLevelFilesAsync(
        string repo, string sha, IReadOnlyList<string> extensions, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/git/trees/{sha}?recursive=1";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));

        var paths = new List<string>();
        foreach (var node in doc.RootElement.GetProperty("tree").EnumerateArray())
        {
            var path = node.GetProperty("path").GetString() ?? "";
            if (path.Contains('/'))
            {
                continue;
            }
            if (extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    public Task<string> DownloadRawAsync(string repo, string sha, string path, CancellationToken ct)
    {
        var url = $"https://raw.githubusercontent.com/{repo}/{sha}/{Uri.EscapeDataString(path)}";
        return http.GetStringAsync(url, ct);
    }
}
