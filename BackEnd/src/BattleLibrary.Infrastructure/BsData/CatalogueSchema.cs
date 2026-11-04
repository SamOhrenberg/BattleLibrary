namespace BattleLibrary.Infrastructure.BsData;

// Per-game-system names the parser keys on, so the same BattleScribe extraction
// logic serves 40k (Unit / Ranged+Melee Weapons / pts) and Kill Team
// (Operative / Weapons / Equipment / no points).
public sealed class CatalogueSchema
{
    public required HashSet<string> StatLineTypes { get; init; }
    public required HashSet<string> WeaponTypes { get; init; }
    public required HashSet<string> RangedWeaponTypes { get; init; }
    public required HashSet<string> AbilityTypes { get; init; }
    public required HashSet<string> EquipmentTypes { get; init; }

    // Cost type name that holds list points ("pts"); empty for point-less systems.
    public required string PointsCostName { get; init; }

    // Selection-group name listing detachments, and the cost-type names for an
    // enhancement counter and a detachment's point cost. Empty on systems
    // without detachments (e.g. Kill Team).
    public string DetachmentGroupName { get; init; } = "";
    public string EnhancementCostName { get; init; } = "";
    public string DetachmentPointsCostName { get; init; } = "";

    // Selection-group name listing the army-wide Force Disposition options in the
    // game system root. Empty on systems without it (e.g. Kill Team).
    public string DispositionGroupName { get; init; } = "";

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);

    public static readonly CatalogueSchema Wh40k = new()
    {
        StatLineTypes = Set("Unit"),
        WeaponTypes = Set("Ranged Weapons", "Melee Weapons"),
        RangedWeaponTypes = Set("Ranged Weapons"),
        AbilityTypes = Set("Abilities"),
        EquipmentTypes = Set(),
        PointsCostName = "pts",
        DetachmentGroupName = "Detachment",
        EnhancementCostName = "Enhancements",
        DetachmentPointsCostName = "Detachment Points",
        DispositionGroupName = "Force Disposition",
    };

    public static readonly CatalogueSchema KillTeam2024 = new()
    {
        StatLineTypes = Set("Operative"),
        WeaponTypes = Set("Weapons"),
        RangedWeaponTypes = Set(),
        AbilityTypes = Set("Abilities"),
        EquipmentTypes = Set("Equipment"),
        PointsCostName = "",
    };
}
