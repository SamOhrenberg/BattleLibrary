using System.Text.Json;
using System.Text.RegularExpressions;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData.Json;

namespace BattleLibrary.Infrastructure.BsData;

// Reads the game system's roster legality caps straight from its primary force
// entry, so the builder no longer needs hardcoded battle-size tables.
//
// In BSData the force entry (40k's "Army Roster") carries a base "max" constraint
// for each budgeted cost - points, Detachment Points, Enhancements - and a set of
// modifiers that raise those caps when a battle-size config entry is selected.
// A modifier targets a constraint by naming the constraint's id in its field, and
// is gated on conditions that reference the battle-size entries. We enumerate the
// battle sizes from the points modifiers, then evaluate each cost's modifiers with
// exactly that size selected (and every optional toggle off) to derive its caps.
internal static partial class RosterLimitsReader
{
    public static RosterLimits Read(DataDocument system, CatalogueSchema schema, string ptsTypeId)
    {
        // Point-less systems (Kill Team) declare no battle sizes or budgets.
        if (string.IsNullOrEmpty(ptsTypeId))
        {
            return RosterLimits.None;
        }

        // The primary force slot - the same one ForceEntryId is taken from.
        var force = system.ForceEntries.FirstOrDefault();
        if (force is null)
        {
            return RosterLimits.None;
        }

        var dpCostId = CostTypeId(system, schema.DetachmentPointsCostName);
        var enhCostId = CostTypeId(system, schema.EnhancementCostName);

        var ptsConstraint = MaxConstraintFor(force, ptsTypeId);
        var dpConstraint = MaxConstraintFor(force, dpCostId);
        var enhConstraint = MaxConstraintFor(force, enhCostId);

        var battleSizes = ReadBattleSizes(force, ptsConstraint, dpConstraint, enhConstraint);
        var categoryLimits = ReadCategoryLimits(force);

        return battleSizes.Count == 0 && categoryLimits.Count == 0
            ? RosterLimits.None
            : new RosterLimits(battleSizes, categoryLimits);
    }

    private static IReadOnlyList<BattleSizeLimits> ReadBattleSizes(
        ForceEntry force, ConstraintNode? pts, ConstraintNode? dp, ConstraintNode? enh)
    {
        if (pts is null)
        {
            return [];
        }

        // Each "set" modifier on the points constraint defines a battle size: its
        // gating "this size is selected" condition names the config entry, and its
        // value is that size's points cap.
        var sizes = new List<(string Id, string Name)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in force.Modifiers.Where(m => Targets(m, pts) && IsSet(m)))
        {
            foreach (var cond in SelectionConditions(mod))
            {
                if (!string.IsNullOrEmpty(cond.ChildId) && seen.Add(cond.ChildId))
                {
                    sizes.Add((cond.ChildId, CleanName(cond.ChildName)));
                }
            }
        }

        return sizes
            .Select(s =>
            {
                var selected = new HashSet<string>(StringComparer.Ordinal) { s.Id };
                return new BattleSizeLimits(
                    s.Name,
                    Cap(EffectiveValue(force, pts, selected)),
                    Cap(EffectiveValue(force, dp, selected)),
                    Cap(EffectiveValue(force, enh, selected)));
            })
            .OrderBy(b => b.PointsCap ?? int.MaxValue)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // A constraint's effective value with the given battle size selected: the base
    // value, then every satisfied "set" modifier applied in document order.
    private static double? EffectiveValue(ForceEntry force, ConstraintNode? constraint, ISet<string> selected)
    {
        if (constraint is null)
        {
            return null;
        }
        var value = constraint.Value;
        foreach (var mod in force.Modifiers)
        {
            if (Targets(mod, constraint) && IsSet(mod) && ModifierApplies(mod, selected)
                && ModifierValue(mod) is { } v)
            {
                value = v;
            }
        }
        return value;
    }

    private static IReadOnlyList<CategoryLimit> ReadCategoryLimits(ForceEntry force)
    {
        var limits = new List<CategoryLimit>();
        foreach (var link in force.CategoryLinks)
        {
            int? min = null, max = null;
            foreach (var c in link.Constraints.Where(c => IsSelectionBound(c)))
            {
                if (string.Equals(c.Type, "min", StringComparison.OrdinalIgnoreCase))
                {
                    min = Cap(c.Value);
                }
                else if (string.Equals(c.Type, "max", StringComparison.OrdinalIgnoreCase))
                {
                    max = Cap(c.Value);
                }
            }
            if (min is not null || max is not null)
            {
                limits.Add(new CategoryLimit(link.TargetId, link.Name, min, max));
            }
        }
        return limits;
    }

    // ---- condition evaluation ----

    // A modifier applies when all its top-level conditions and condition groups
    // hold, under the assumption that exactly the selected config entries are
    // present (value 1) and everything else - including optional toggles - is 0.
    private static bool ModifierApplies(ModifierNode mod, ISet<string> selected) =>
        mod.Conditions.All(c => ConditionHolds(c, selected))
        && mod.ConditionGroups.All(g => GroupHolds(g, selected));

    private static bool GroupHolds(ConditionGroupNode group, ISet<string> selected)
    {
        var isOr = string.Equals(group.Type, "or", StringComparison.OrdinalIgnoreCase);
        var parts = group.Conditions.Select(c => ConditionHolds(c, selected))
            .Concat(group.ConditionGroups.Select(g => GroupHolds(g, selected)));
        // An empty "and" is vacuously true; an empty "or" contributes nothing.
        return isOr ? parts.Any(p => p) : parts.All(p => p);
    }

    private static bool ConditionHolds(ConditionNode cond, ISet<string> selected)
    {
        double actual = selected.Contains(cond.ChildId) ? 1 : 0;
        return cond.Type.ToLowerInvariant() switch
        {
            "equalto" => actual == cond.Value,
            "notequalto" => actual != cond.Value,
            "atleast" or "greaterthanorequalto" => actual >= cond.Value,
            "atmost" or "lessthanorequalto" => actual <= cond.Value,
            "greaterthan" => actual > cond.Value,
            "lessthan" => actual < cond.Value,
            // Types we don't model (instanceOf, …) can't be judged here; treat as
            // unmet so a cap is never raised on an assumption we can't verify.
            _ => false,
        };
    }

    // The conditions of a "set" modifier that assert a config entry is present
    // (value >= 1): these name the battle-size entry the modifier is gated on,
    // as opposed to "off" gates like "Override points limit? == 0".
    private static IEnumerable<ConditionNode> SelectionConditions(ModifierNode mod)
    {
        IEnumerable<ConditionNode> Walk(IEnumerable<ConditionNode> conds, IEnumerable<ConditionGroupNode> groups)
        {
            foreach (var c in conds)
            {
                if (c.Value >= 1 && (c.Type.Equals("equalTo", StringComparison.OrdinalIgnoreCase)
                        || c.Type.Equals("atLeast", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return c;
                }
            }
            foreach (var g in groups)
            {
                foreach (var c in Walk(g.Conditions, g.ConditionGroups))
                {
                    yield return c;
                }
            }
        }
        return Walk(mod.Conditions, mod.ConditionGroups);
    }

    // ---- small helpers ----

    private static string CostTypeId(DataDocument system, string name) =>
        string.IsNullOrEmpty(name)
            ? ""
            : system.CostTypes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

    private static ConstraintNode? MaxConstraintFor(ForceEntry force, string costTypeId) =>
        string.IsNullOrEmpty(costTypeId)
            ? null
            : force.Constraints.FirstOrDefault(c =>
                c.Field == costTypeId && string.Equals(c.Type, "max", StringComparison.OrdinalIgnoreCase));

    private static bool IsSelectionBound(ConstraintNode c) =>
        string.Equals(c.Field, "selections", StringComparison.OrdinalIgnoreCase);

    private static bool Targets(ModifierNode mod, ConstraintNode constraint) =>
        !string.IsNullOrEmpty(constraint.Id) && mod.Field == constraint.Id;

    private static bool IsSet(ModifierNode mod) =>
        string.Equals(mod.Type, "set", StringComparison.OrdinalIgnoreCase);

    private static double? ModifierValue(ModifierNode mod) =>
        mod.Value.ValueKind == JsonValueKind.Number && mod.Value.TryGetDouble(out var v) ? v : null;

    // BSData uses -1 for "no limit"; surface that as null.
    private static int? Cap(double? value) =>
        value is null or < 0 ? null : (int)Math.Round(value.Value);

    // "1. Incursion (1000 Point limit)" -> "Incursion": drop a leading ordinal
    // ("1. ") and a trailing parenthetical, without hardcoding the size names.
    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name ?? "";
        }
        var trimmed = OrdinalPrefix().Replace(name, "");
        trimmed = ParentheticalSuffix().Replace(trimmed, "");
        return trimmed.Trim();
    }

    [GeneratedRegex(@"^\s*\d+\.\s*")]
    private static partial Regex OrdinalPrefix();

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex ParentheticalSuffix();
}
