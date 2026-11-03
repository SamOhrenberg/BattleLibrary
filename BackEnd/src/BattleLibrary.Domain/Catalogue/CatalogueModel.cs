namespace BattleLibrary.Domain.Catalogue;

// Slim, GW-agnostic read model produced from BSData. Intentionally minimal:
// the functional data for list building, no lore or full rules text.

// PointsCostTypeId is the BSData cost-type id for list points ("pts"), needed to
// write roster (.ros) cost nodes; empty on point-less systems. ForceEntryId/Name
// identify the system's primary roster force slot, which a roster's <force> must
// reference by id or importers reject it.
public sealed record GameSystemInfo(
    string Id, string Name, int Revision, string PointsCostTypeId = "",
    string ForceEntryId = "", string ForceEntryName = "");

public sealed record CatalogueModel(
    GameSystemInfo GameSystem,
    string FactionId,
    string FactionName,
    int FactionRevision,
    IReadOnlyList<Datasheet> Datasheets,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<Detachment> Detachments);

// A detachment choice, its Detachment Points cost, the enhancements it offers
// (each with its pts cost), and its detachment rules (name + text).
public sealed record Detachment(
    string Id, string Name, int Dp, IReadOnlyList<Enhancement> Enhancements, IReadOnlyList<Ability> Rules);

public sealed record Enhancement(string Id, string Name, int Points, string Text);

// A named rule with its text: a datasheet ability, a detachment rule, etc.
public sealed record Ability(string Name, string Text);

public sealed record Datasheet(
    string Id,
    string Name,
    int? Points,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<UnitProfile> Profiles,
    IReadOnlyList<WeaponProfile> Weapons,
    IReadOnlyList<Ability> Abilities,
    IReadOnlyList<DatasheetOption> Options,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<UnitSize> Sizes,
    IReadOnlyList<ModelLoadout> ModelGroups,
    IReadOnlyList<ModelVariantGroup> VariantGroups,
    int FixedModelCount,
    DatasheetAvailability Availability);

// When a datasheet is selectable, from its BattleScribe "hidden" modifiers.
// RequiredDetachmentIds: shown only when one of these detachments is selected
// (e.g. Scintillating Legions units need Changehost of Deceit); empty = any
// detachment. RequiredToggles: "Show X" opt-in config toggles that must be
// enabled for it to appear (off by default), by toggle name; empty = always on.
public sealed record DatasheetAvailability(
    IReadOnlyList<string> RequiredDetachmentIds,
    IReadOnlyList<string> RequiredToggles)
{
    public static readonly DatasheetAvailability None = new([], []);
}

// A selectable unit size: how many models and the points for that size.
public sealed record UnitSize(int Models, int Points);

// A model type within a unit (e.g. "9-18 Boyz") and its default weapons.
public sealed record ModelLoadout(string Label, IReadOnlyList<string> Weapons);

// A distributable model pool (e.g. Rubric Marines' "4-9 Rubric Marines"): the
// player splits the pool across these weapon variants. Max is the pool cap.
public sealed record ModelVariantGroup(
    string Id, string Name, int Max, IReadOnlyList<ModelVariant> Variants);

// One weapon loadout within a variant group. Max is the per-variant cap; Points
// is the additive pts cost per model taking it (usually 0 in 11e).
public sealed record ModelVariant(
    string Id, string Name, int Max, int Points, IReadOnlyList<WeaponProfile> Weapons);

// A selectable wargear/upgrade choice. Points is the additive pts cost (0 when
// the option is free, as most 11e wargear is). Group is the choice set it
// belongs to (e.g. "Melee Weapons"), for presentation. Weapons holds the stat
// line(s) the choice grants, when it is a weapon (empty for plain upgrades).
// Model is the model type the choice is for (e.g. "Aspiring Sorcerer"), empty
// when it applies unit-wide or the unit is a single model.
//
// GroupId is the choice set's stable id, GroupMin/GroupMax its selection bounds
// (how many of the set may be taken): GroupMax 1 is a "pick one" swap, GroupMax
// 0 means no declared cap. These drive single-select enforcement in the builder.
public sealed record DatasheetOption(
    string Id, string Name, int Points, string Group, IReadOnlyList<WeaponProfile> Weapons,
    string Model = "", string GroupId = "", int GroupMin = 0, int GroupMax = 0);

public sealed record UnitProfile(string Name, IReadOnlyList<Characteristic> Characteristics);

public sealed record WeaponProfile(string Name, bool Ranged, IReadOnlyList<Characteristic> Characteristics);

public sealed record Characteristic(string Name, string Value);
