namespace BattleLibrary.Domain.Lists;

// The fixed set of game systems a list or game can be tagged with.
public static class GameSystems
{
    public const string Warhammer40k = "wh40k";
    public const string KillTeam = "kill-team";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> Keys = [Warhammer40k, KillTeam, Other];

    // Returns the key if known, else Other.
    public static string Normalize(string? key) =>
        Keys.FirstOrDefault(k => string.Equals(k, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Other;
}
