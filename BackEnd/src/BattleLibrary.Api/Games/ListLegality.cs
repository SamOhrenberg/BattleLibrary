using BattleLibrary.Domain.Lists;

namespace BattleLibrary.Api.Games;

// Mirrors the list builder's 40k 11e legality checks (frontend validation.ts) so
// a list the builder flags can't be brought into a live game. Returns the first
// problem as a short reason, or null when the list is legal.
public static class ListLegality
{
    private readonly record struct SizeRules(int Dp, int Enhancements, int RuleOfN);

    // int.MaxValue stands in for "no limit" (Custom lists; Dedicated Transports).
    private static readonly SizeRules Default = new(int.MaxValue, 4, 3);
    private static readonly IReadOnlyDictionary<string, SizeRules> BySize = new Dictionary<string, SizeRules>
    {
        ["Incursion"] = new(2, 2, 2),
        ["Strike Force"] = new(3, 4, 3),
        ["Onslaught"] = new(5, 4, 3),
    };

    public static string? FirstIssue(ArmyList list, bool hasDetachments, bool pointless)
    {
        // Point-less systems (Kill Team) have no detachment/warlord/enhancement rules.
        if (pointless)
        {
            return null;
        }
        var rules = list.BattleSize is { } size && BySize.TryGetValue(size, out var r) ? r : Default;

        if (hasDetachments)
        {
            if (list.Detachments.Count == 0)
            {
                return "no detachment is selected.";
            }
            // A single detachment is always legal even over budget.
            var dpUsed = list.Detachments.Sum(d => d.Dp);
            if (list.Detachments.Count > 1 && dpUsed > rules.Dp)
            {
                return $"its detachments cost {dpUsed} DP, over the {rules.Dp} DP budget.";
            }
        }

        var forced = list.Entries.Count(e => e.ForcedWarlord);
        if (forced > 1)
        {
            return "two units each require being the Warlord.";
        }
        var warlords = list.Entries.Count(e => e.IsWarlord);
        if (warlords == 0)
        {
            return "no Warlord is assigned.";
        }
        if (warlords > 1)
        {
            return "more than one Warlord is assigned.";
        }

        var enhanced = list.Entries.Count(e => e.EnhancementId is not null);
        if (enhanced > rules.Enhancements)
        {
            return $"it has {enhanced} enhancements, over the limit of {rules.Enhancements}.";
        }

        // Rule of N: ≤N of a datasheet, Battleline ≤2N, Dedicated Transport
        // unlimited, Epic Hero ≤1. Each entry is one occurrence of its datasheet.
        foreach (var g in list.Entries.GroupBy(e => e.DatasheetId))
        {
            var sample = g.First();
            var max = HasKeyword(sample, "Dedicated Transport") ? int.MaxValue
                : HasKeyword(sample, "Epic Hero") ? 1
                : HasKeyword(sample, "Battleline") ? rules.RuleOfN * 2
                : rules.RuleOfN;
            var n = g.Count();
            if (n > max)
            {
                return $"it has {n}× {sample.DatasheetName}, over the max of {max}.";
            }
        }

        return null;
    }

    private static bool HasKeyword(ListEntry e, string kw) =>
        e.Keywords.Any(k => string.Equals(k, kw, StringComparison.OrdinalIgnoreCase));
}
