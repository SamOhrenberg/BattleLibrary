using BattleLibrary.Domain.Accounts;
using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.Feedback;

public enum FeedbackKind
{
    Bug,
    Idea,
    Data,
    Other,
}

// Feedback a user sent from the app, filed as a GitHub issue. The issue is the
// source of truth; State and the counters are a cache from the last sync.
public class FeedbackReport : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public FeedbackKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Area { get; set; }

    public int IssueNumber { get; set; }
    public string IssueUrl { get; set; } = string.Empty;

    // GitHub's "open" | "closed", and for closed issues "completed" | "not_planned" | "duplicate".
    public string State { get; set; } = "open";
    public string? StateReason { get; set; }
    public List<string> Labels { get; set; } = [];
    public int CommentCount { get; set; }
    public DateTimeOffset IssueUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
