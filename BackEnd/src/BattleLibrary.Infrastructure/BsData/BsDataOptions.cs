namespace BattleLibrary.Infrastructure.BsData;

public enum SourceFormat
{
    Json,
    Xml,
}

// One upstream BSData repo mapped to a cached game system. The schema names the
// profile/cost types the parser keys on; FilePrefix narrows a multi-edition repo
// to one edition's files (e.g. "2024 - " in the Kill Team repo).
public sealed record BsDataSource(
    string SystemKey,
    string DisplayName,
    string Repo,
    string Branch,
    SourceFormat Format,
    string FilePrefix,
    CatalogueSchema Schema)
{
    public static readonly BsDataSource Wh40k11e = new(
        "wh40k-11e", "Warhammer 40,000 (11th Edition)", "BSData/wh40k-11e", "main",
        SourceFormat.Json, "", CatalogueSchema.Wh40k);

    public static readonly BsDataSource KillTeam2024 = new(
        "kill-team-2024", "Kill Team (2024)", "BSData/wh40k-killteam", "master",
        SourceFormat.Xml, "2024 - ", CatalogueSchema.KillTeam2024);

    // Systems loaded into the in-memory catalogue. 40k is JSON; Kill Team is BattleScribe XML.
    public static readonly IReadOnlyList<BsDataSource> All = [Wh40k11e, KillTeam2024];
}

public sealed class BsDataOptions
{
    public const string SectionName = "BsData";

    // The catalogue lives only in memory, so it is loaded at every startup.
    // Off only for offline work, where the catalogue stays empty.
    public bool LoadOnStartup { get; set; } = true;

    // Background polling of every source for new commits on its branch.
    public bool AutoRefreshEnabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 1;

    // Optional personal access token. Raises the GitHub API rate limit from
    // 60/hr (unauthenticated, per IP) to 5000/hr. Recommended in production.
    public string GitHubToken { get; set; } = "";
}
