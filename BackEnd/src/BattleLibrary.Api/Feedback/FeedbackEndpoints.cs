using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using BattleLibrary.Api.Hosting;
using BattleLibrary.Domain.Feedback;
using BattleLibrary.Infrastructure.Feedback;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Feedback;

// In-app feedback, filed as issues on the project's GitHub repo. Each report
// keeps a cached copy of the issue state, refreshed when it goes stale.
public static partial class FeedbackEndpoints
{
    // Mirrors the Area dropdown in .github/ISSUE_TEMPLATE/bug_report.yml.
    public static readonly string[] Areas =
    [
        "List building (40k)", "Kill Team rosters", "Live games", "Game history",
        "Friends and profiles", "Sign in / registration", "Import / export", "Rule search", "Other",
    ];

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private const int MaxSyncPerRequest = 25;

    // Separates the user's text from the metadata footer in the issue body.
    private const string MetaMarker = "<!-- battle-library-meta -->";

    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder routes)
    {
        var feedback = routes.MapGroup("/api/feedback").RequireAuthorization();
        feedback.MapGet("", GetMine);
        feedback.MapPost("", Submit).RequireRateLimiting(HostingExtensions.FeedbackPolicy);
        feedback.MapGet("/{id:guid}", GetOne);
    }

    private static async Task<IResult> GetMine(
        ClaimsPrincipal principal, BattleLibraryDbContext db, FeedbackOptions options,
        GitHubIssuesClient github, ILoggerFactory logs, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var reports = await db.FeedbackReports
            .Where(f => f.UserId == me)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        if (options.Enabled)
        {
            var now = DateTimeOffset.UtcNow;
            var stale = reports.Where(r => now - r.SyncedAt > StaleAfter).Take(MaxSyncPerRequest).ToList();
            var log = logs.CreateLogger("Feedback");
            await Task.WhenAll(stale.Select(async r =>
            {
                try
                {
                    Apply(r, await github.GetAsync(r.IssueNumber, ct));
                }
                catch (Exception ex) when (IsGitHubFailure(ex, ct))
                {
                    log.LogWarning(ex, "Could not sync feedback issue #{IssueNumber}", r.IssueNumber);
                }
            }));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new FeedbackListDto(options.Enabled, reports.Select(ToDto).ToList()));
    }

    private static async Task<IResult> GetOne(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, FeedbackOptions options,
        GitHubIssuesClient github, ILoggerFactory logs, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        var report = await db.FeedbackReports.FirstOrDefaultAsync(f => f.Id == id && f.UserId == me, ct);
        if (report is null)
        {
            return Results.NotFound();
        }
        if (!options.Enabled)
        {
            return Results.Ok(new FeedbackDetailDto(ToDto(report), null, [], Live: false));
        }

        try
        {
            var issueTask = github.GetAsync(report.IssueNumber, ct);
            var commentsTask = github.GetCommentsAsync(report.IssueNumber, ct);
            var issue = await issueTask;
            var comments = await commentsTask;

            Apply(report, issue);
            await db.SaveChangesAsync(ct);

            var updates = comments
                .Where(c => !string.IsNullOrWhiteSpace(c.Body))
                .Select(c => new FeedbackCommentDto(
                    c.Id, c.User?.Login ?? "ghost", IsMaintainer(c.AuthorAssociation), c.Body!, c.CreatedAt))
                .ToList();
            return Results.Ok(new FeedbackDetailDto(ToDto(report), UserText(issue.Body), updates, Live: true));
        }
        catch (Exception ex) when (IsGitHubFailure(ex, ct))
        {
            logs.CreateLogger("Feedback").LogWarning(ex, "Could not load feedback issue #{IssueNumber}", report.IssueNumber);
            return Results.Ok(new FeedbackDetailDto(ToDto(report), null, [], Live: false));
        }
    }

    private static async Task<IResult> Submit(
        SubmitFeedbackRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db,
        FeedbackOptions options, GitHubIssuesClient github, ILoggerFactory logs, CancellationToken ct)
    {
        if (UserId(principal) is not { } me)
        {
            return Results.Unauthorized();
        }
        if (!options.Enabled)
        {
            return Results.Problem("Feedback isn't set up on this server yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        if (!Enum.TryParse<FeedbackKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return Problem("kind", "Pick a feedback type.");
        }
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 5 or > 120)
        {
            return Problem("title", "The title must be 5 to 120 characters.");
        }
        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is < 10 or > 5000)
        {
            return Problem("description", "The description must be 10 to 5000 characters.");
        }
        var area = string.IsNullOrWhiteSpace(request.Area) ? null : request.Area.Trim();
        if (area is not null && !Areas.Contains(area))
        {
            return Problem("area", "Pick an area from the list.");
        }

        var report = new FeedbackReport { UserId = me, Kind = kind, Title = title, Area = area };
        var body = IssueBody(report, description, request.Device);
        string[] labels = LabelFor(kind) is { } kindLabel ? [kindLabel, options.Label] : [options.Label];

        GitHubIssue issue;
        try
        {
            issue = await github.CreateAsync(NeutralizeMentions(title), body, labels, ct);
        }
        catch (Exception ex) when (IsGitHubFailure(ex, ct))
        {
            logs.CreateLogger("Feedback").LogError(ex, "Could not create a GitHub issue for feedback");
            return Results.Problem("Couldn't reach GitHub to file your feedback. Try again in a moment.", statusCode: StatusCodes.Status502BadGateway);
        }

        report.IssueNumber = issue.Number;
        report.IssueUrl = issue.HtmlUrl;
        Apply(report, issue);
        db.FeedbackReports.Add(report);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/feedback/{report.Id}", ToDto(report));
    }

    private static string IssueBody(FeedbackReport report, string description, string? device)
    {
        var body = new StringBuilder()
            .AppendLine(NeutralizeMentions(description))
            .AppendLine()
            .AppendLine(MetaMarker)
            .AppendLine("---")
            .AppendLine($"**Type:** {report.Kind}  ");
        if (report.Area is not null)
        {
            body.AppendLine($"**Area:** {report.Area}  ");
        }
        var agent = device?.Replace("`", "").Trim();
        if (!string.IsNullOrEmpty(agent))
        {
            body.AppendLine($"**Device:** `{agent[..Math.Min(agent.Length, 300)]}`  ");
        }
        return body
            .AppendLine("_Submitted from The Battle Library app._")
            .AppendLine($"<!-- battle-library-feedback:{report.Id} -->")
            .ToString();
    }

    // The issue body minus the metadata footer, i.e. what the user wrote.
    private static string? UserText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        var cut = body.IndexOf(MetaMarker, StringComparison.Ordinal);
        return (cut >= 0 ? body[..cut] : body).Trim().Replace("@​", "@");
    }

    private static void Apply(FeedbackReport report, GitHubIssue issue)
    {
        report.State = issue.State;
        report.StateReason = issue.StateReason;
        report.Labels = issue.Labels.Select(l => l.Name).ToList();
        report.CommentCount = issue.Comments;
        report.IssueUpdatedAt = issue.UpdatedAt;
        report.SyncedAt = DateTimeOffset.UtcNow;
    }

    private static FeedbackSummaryDto ToDto(FeedbackReport r) => new(
        r.Id, r.Kind.ToString(), r.Title, r.Area, r.IssueNumber, r.IssueUrl,
        StatusFor(r), r.Labels, r.CommentCount, r.CreatedAt, r.IssueUpdatedAt);

    private static string StatusFor(FeedbackReport r) => (r.State, r.StateReason) switch
    {
        ("open", _) => "open",
        (_, "not_planned") => "not_planned",
        (_, "duplicate") => "duplicate",
        _ => "done",
    };

    private static string? LabelFor(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Bug => "bug",
        FeedbackKind.Idea => "enhancement",
        FeedbackKind.Data => "data",
        _ => null,
    };

    // Network errors, non-2xx responses, timeouts, and unexpected payloads; not client aborts.
    private static bool IsGitHubFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    private static bool IsMaintainer(string association) =>
        association is "OWNER" or "MEMBER" or "COLLABORATOR";

    // A zero-width space after '@' stops user text from pinging GitHub accounts.
    private static string NeutralizeMentions(string text) => MentionPattern().Replace(text, "@​");

    [GeneratedRegex(@"@(?=[A-Za-z0-9])")]
    private static partial Regex MentionPattern();

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
