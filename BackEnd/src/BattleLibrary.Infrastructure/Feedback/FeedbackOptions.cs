namespace BattleLibrary.Infrastructure.Feedback;

public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    // "owner/name" of the repo that receives in-app feedback as issues.
    public string Repository { get; set; } = "SamOhrenberg/BattleLibrary";

    // Fine-grained PAT scoped to Repository with Issues read/write. Empty disables submission.
    public string GitHubToken { get; set; } = "";

    // Label added to every in-app issue, alongside the kind's label.
    public string Label { get; set; } = "in-app";

    public bool Enabled => !string.IsNullOrWhiteSpace(GitHubToken);
}
