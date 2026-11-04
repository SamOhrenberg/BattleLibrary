namespace BattleLibrary.Domain.Catalogue;

// Slim, GW-agnostic read model produced from BSData. Intentionally minimal:
// the functional data for list building, no lore or full rules text.

// PointsCostTypeId is the BSData cost-type id for list points ("pts"), needed to
// write roster (.ros) cost nodes; empty on point-less systems. ForceEntryId/Name
// identify the system's primary roster force slot, which a roster's <force> must
// reference by id or importers reject it. Limits carries the data-driven roster
// legality caps read from that force (null on systems that declare none).
// Dispositions are the army-wide Force Disposition options (same for every
// faction); empty on systems that declare none.
public sealed record GameSystemInfo(
    string Id, string Name, int Revision, string PointsCostTypeId = "",
    string ForceEntryId = "", string ForceEntryName = "", RosterLimits? Limits = null,
    IReadOnlyList<Disposition> Dispositions = null!)
{
    // Empty default so older cached models deserialize without a null list.
    public IReadOnlyList<Disposition> Dispositions { get; init; } = Dispositions ?? [];
}

// An army-wide "Force Disposition" choice (40k 11e): a roster-level configuration
// option read from the game system root. Id/Name are the stable BSData
// selection-entry id and display name. CategoryId is the category that unlocks
// it (granted by the chosen detachment); used to tie dispositions to detachments.
public sealed record Disposition(string Id, string Name, string CategoryId = "");

// Data-driven roster legality limits read from the game system's primary force
// entry, so the builder need not hardcode battle-size tables. BattleSizes lists
// each battle size with the caps derived after applying that size's modifiers;
// CategoryLimits holds per-category caps (e.g. battlefield-role counts) that
// apply regardless of size.
public sealed record RosterLimits(
    IReadOnlyList<BattleSizeLimits> BattleSizes,
    IReadOnlyList<CategoryLimit> CategoryLimits)
{
    public static readonly RosterLimits None = new([], []);
}

// Caps for a single battle size, derived from the force constraints after that
// size's modifiers are applied. A null cap means unlimited (BattleScribe -1).
public sealed record BattleSizeLimits(
    string Name, int? PointsCap, int? DetachmentPointsCap, int? EnhancementCap);

// A per-category roster cap (e.g. at most N of a battlefield role, at least one
// Character). Min/Max are null when that bound is unset.
public sealed record CategoryLimit(string CategoryId, string Name, int? Min, int? Max);

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
// DispositionIds are the Force Disposition option ids this detachment unlocks
// (via its category links); empty when it grants none.
public sealed record Detachment(
    string Id, string Name, int Dp, IReadOnlyList<Enhancement> Enhancements, IReadOnlyList<Ability> Rules,
    IReadOnlyList<string> DispositionIds = null!)
{
    // Empty default so older cached jsonb deserializes without a null list.
    public IReadOnlyList<string> DispositionIds { get; init; } = DispositionIds ?? [];
}

public sealed record Enhancement(string Id, string Name, int Points, string Text);

// A named rule with its text: a datasheet ability, a detachment rule, etc.
// Source, when known, cites the book and page it is printed in.
public sealed record Ability(string Name, string Text, Citation? Source = null);

// A source citation for a datasheet or ability: the book (its short name where
// available, e.g. "Orks") and the page within it. Page may be empty when only
// the publication is known.
public sealed record Citation(string Publication, string Page);

public sealed record Datasheet(
    string Id,
    string Name,
    int? Points,
    IReadOnlyList<string> Keywords,
    // The unit's primary category (its battlefield role / faction keyword), from
    // the categoryLink flagged primary in BSData. Also the first entry of Keywords
    // (the parser orders it first), surfaced here too so callers that have only
    // this field can read it directly. Empty when none is flagged primary.
    string PrimaryKeyword,
    IReadOnlyList<UnitProfile> Profiles,
    IReadOnlyList<WeaponProfile> Weapons,
    // The datasheet's own abilities (ability-type profiles). Referenced core and
    // keyword rules (Deep Strike, Devastating Wounds, ...) are kept apart in Rules.
    IReadOnlyList<Ability> Abilities,
    IReadOnlyList<DatasheetOption> Options,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<UnitSize> Sizes,
    IReadOnlyList<ModelLoadout> ModelGroups,
    IReadOnlyList<ModelVariantGroup> VariantGroups,
    int FixedModelCount,
    DatasheetAvailability Availability,
    IReadOnlyList<LeaderAttachment> LeaderAttachments,
    IReadOnlyList<CostRule> CostRules = null!,
    // Upstream display order (BSData sortIndex); long.MaxValue when the source
    // omits it, so unindexed units sort after indexed ones, name breaking ties.
    long SortIndex = long.MaxValue,
    // The book and page this datasheet is printed in, from its stat profile;
    // null when the source data carries no citation.
    Citation? Source = null)
{
    // Default to empty so older cached jsonb (written before cost rules existed)
    // deserializes without a null list.
    public IReadOnlyList<CostRule> CostRules { get; init; } = CostRules ?? [];

    // Keyword changes that only apply under a condition, from the datasheet's
    // "category" modifiers (e.g. a unit gaining Battleline under a specific
    // detachment). Unconditional category modifiers are already folded into
    // <see cref="Keywords"/>; these are the ones the builder must apply (or not)
    // based on the selected detachment / config toggle, feeding the Battleline
    // and rule-of-N checks. Empty for most units. Init-only with an empty default
    // so older cached models deserialize unchanged.
    public IReadOnlyList<ConditionalKeyword> ConditionalKeywords { get; init; } = [];

    // Referenced core/keyword rules for the unit (Deep Strike, Deadly Demise,
    // Devastating Wounds, ...), resolved from rule infoLinks. Kept separate from
    // the datasheet's own Abilities. Empty default so older cached jsonb (written
    // before the split) deserializes with no rules.
    public IReadOnlyList<Ability> Rules { get; init; } = [];
}

// A selection-dependent points adjustment, used for Crucible warband characters
// whose price scales with the weapons chosen. CountedOptionIds are the datasheet
// option ids the rule counts (selections in a linked group). Amount is signed
// (a decrement is negative).
//
// PerSelection: add Amount once for every Step selected (Step is 1 for a plain
// "+X per weapon"). Threshold (Step 0): add Amount once when the selected count
// satisfies Operator against Value (e.g. atLeast 3). Operator is one of
// atLeast / greaterThan / atMost / lessThan / equalTo.
public sealed record CostRule(
    IReadOnlyList<string> CountedOptionIds,
    int Amount,
    bool PerSelection,
    int Step,
    string Operator,
    int Value);

// A keyword a datasheet gains (Add = true) or loses (Add = false) only when a
// gate is active. RequiredDetachmentIds: the change applies when one of these
// detachments is selected. RequiredToggles: or when one of these "Show X" config
// toggles is on. Exactly one gate is recorded per entry (an AND of several gates
// can't be modelled flatly), named by the same ids/toggles as
// <see cref="DatasheetAvailability"/>.
public sealed record ConditionalKeyword(
    string Keyword,
    bool Add,
    IReadOnlyList<string> RequiredDetachmentIds,
    IReadOnlyList<string> RequiredToggles);

// Leader attachment (11e): which units this datasheet - a Leader character -
// can attach to. Label is the association label ("Leader", "Supported by");
// Targets are the names of the units/categories it may lead. Empty for
// non-character datasheets and characters that lead nothing.
public sealed record LeaderAttachment(string Label, IReadOnlyList<string> Targets);

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
// is the additive pts cost per model taking it (usually 0 in 11e). IsDefault
// marks the loadout the pool fills with first (the group's declared default).
public sealed record ModelVariant(
    string Id, string Name, int Max, int Points, IReadOnlyList<WeaponProfile> Weapons, bool IsDefault = false);

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
//
// IsDefault marks a choice pre-selected when the unit is added, so starting
// wargear and points match the datasheet: the choice named by its group's
// defaultSelectionEntryId, or one forced on by its own selection-min. Mandatory
// marks the latter - included by default and not removable (the base loadout).
public sealed record DatasheetOption(
    string Id, string Name, int Points, string Group, IReadOnlyList<WeaponProfile> Weapons,
    string Model = "", string GroupId = "", int GroupMin = 0, int GroupMax = 0,
    bool IsDefault = false, bool Mandatory = false);

public sealed record UnitProfile(string Name, IReadOnlyList<Characteristic> Characteristics);

public sealed record WeaponProfile(string Name, bool Ranged, IReadOnlyList<Characteristic> Characteristics);

public sealed record Characteristic(string Name, string Value);
