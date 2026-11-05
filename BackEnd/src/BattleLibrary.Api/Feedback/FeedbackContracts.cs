namespace BattleLibrary.Api.Feedback;

// Kind: Bug | Idea | Data | Other. Device is the browser's user agent, optional.
public sealed record SubmitFeedbackRequest(string Kind, string Title, string Description, string? Area, string? Device);

// Status: open | done | not_planned | duplicate.
public sealed record FeedbackSummaryDto(
    Guid Id,
    string Kind,
    string Title,
    string? Area,
    int IssueNumber,
    string IssueUrl,
    string Status,
    IReadOnlyList<string> Labels,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// Enabled is false when the server has no GitHub token, so submission is off.
public sealed record FeedbackListDto(bool Enabled, IReadOnlyList<FeedbackSummaryDto> Reports);

public sealed record FeedbackCommentDto(long Id, string Author, bool IsMaintainer, string Body, DateTimeOffset CreatedAt);

// Live is false when GitHub could not be reached and Report is the cached copy.
public sealed record FeedbackDetailDto(
    FeedbackSummaryDto Report,
    string? Description,
    IReadOnlyList<FeedbackCommentDto> Comments,
    bool Live);
