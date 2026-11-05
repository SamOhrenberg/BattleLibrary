using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLibrary.Infrastructure.Feedback;

public sealed record GitHubLabel(string Name);

public sealed record GitHubUser(string Login);

public sealed record GitHubIssue(
    int Number,
    string HtmlUrl,
    string? Body,
    string State,
    string? StateReason,
    int Comments,
    IReadOnlyList<GitHubLabel> Labels,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

public sealed record GitHubComment(
    long Id,
    GitHubUser? User,
    string? Body,
    string AuthorAssociation,
    DateTimeOffset CreatedAt);

// Creates and reads issues on the feedback repo via the GitHub REST API.
public sealed class GitHubIssuesClient(HttpClient http, FeedbackOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<GitHubIssue> CreateAsync(string title, string body, IReadOnlyList<string> labels, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync(
            $"repos/{options.Repository}/issues", new { title, body, labels }, Json, ct);
        res.EnsureSuccessStatusCode();
        return await Read<GitHubIssue>(res, ct);
    }

    public async Task<GitHubIssue> GetAsync(int number, CancellationToken ct)
    {
        using var res = await http.GetAsync($"repos/{options.Repository}/issues/{number}", ct);
        res.EnsureSuccessStatusCode();
        return await Read<GitHubIssue>(res, ct);
    }

    public async Task<IReadOnlyList<GitHubComment>> GetCommentsAsync(int number, CancellationToken ct)
    {
        using var res = await http.GetAsync($"repos/{options.Repository}/issues/{number}/comments?per_page=100", ct);
        res.EnsureSuccessStatusCode();
        return await Read<List<GitHubComment>>(res, ct);
    }

    private static async Task<T> Read<T>(HttpResponseMessage res, CancellationToken ct) =>
        await res.Content.ReadFromJsonAsync<T>(Json, ct)
        ?? throw new InvalidOperationException("GitHub returned an empty response.");
}
