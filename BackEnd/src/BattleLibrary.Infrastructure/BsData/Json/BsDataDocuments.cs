using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLibrary.Infrastructure.BsData.Json;

// Partial mirror of the BSData JSON schema - only the fields the parser reads.

internal sealed class GameSystemRoot
{
    public GameSystemDocument? GameSystem { get; set; }
}

internal sealed class CatalogueRoot
{
    public CatalogueDocument? Catalogue { get; set; }
}

internal abstract class DataDocument
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Revision { get; set; }
    public List<CostType> CostTypes { get; set; } = [];
    public List<ProfileType> ProfileTypes { get; set; } = [];
    public List<CategoryEntry> CategoryEntries { get; set; } = [];
    public List<ForceEntry> ForceEntries { get; set; } = [];
    public List<ProfileNode> SharedProfiles { get; set; } = [];
    public List<RuleNode> SharedRules { get; set; } = [];
    public List<InfoGroupNode> SharedInfoGroups { get; set; } = [];
    public List<SelectionEntryNode> SharedSelectionEntries { get; set; } = [];
    public List<SelectionEntryGroupNode> SharedSelectionEntryGroups { get; set; } = [];
    public List<EntryLinkNode> EntryLinks { get; set; } = [];
}

internal sealed class GameSystemDocument : DataDocument;

internal sealed class CatalogueDocument : DataDocument
{
    public string GameSystemId { get; set; } = "";
    public bool Library { get; set; }
    public List<CatalogueLink> CatalogueLinks { get; set; } = [];
}

internal sealed class CostType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Default per-roster cap on this cost (e.g. a points budget); -1 means no
    // limit. BattleScribe can override it per selection via modifiers.
    public double DefaultCostLimit { get; set; } = -1;
}

internal sealed class ProfileType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class CategoryEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

// A roster force slot defined by the game system (e.g. 40k's "Army Roster").
// A roster's <force> must reference one by its entryId or importers reject it.
// Its constraints carry the roster-level legality limits (enhancement cap,
// Detachment Points cap, per-category caps); modifiers adjust those per the
// selected battle size, and nested forceEntries describe sub-forces.
internal sealed class ForceEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<ConstraintNode> Constraints { get; set; } = [];
    public List<CategoryLink> CategoryLinks { get; set; } = [];
    public List<ModifierNode> Modifiers { get; set; } = [];
    public List<ModifierGroupNode> ModifierGroups { get; set; } = [];
    public List<ForceEntry> ForceEntries { get; set; } = [];
}

internal sealed class CatalogueLink
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TargetId { get; set; } = "";

    // True when the link pulls in the target catalogue's root entries wholesale
    // (a chapter importing the base Space Marines catalogue), not just referenced
    // entries. Libraries are imported regardless of this flag.
    public bool ImportRootEntries { get; set; }
}

internal sealed class CategoryLink
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TargetId { get; set; } = "";
    public bool Primary { get; set; }

    // On a force's categoryLink, min/max bounds cap how many of that category the
    // roster may hold (a data-driven per-category limit).
    public List<ConstraintNode> Constraints { get; set; } = [];
}

internal sealed class Cost
{
    public string Name { get; set; } = "";
    public string TypeId { get; set; } = "";
    public double Value { get; set; }
}

internal sealed class CharacteristicNode
{
    public string Name { get; set; } = "";
    public string TypeId { get; set; } = "";

    [JsonPropertyName("$text")]
    public string? Text { get; set; }
}

internal sealed class ProfileNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeId { get; set; } = "";
    public string TypeName { get; set; } = "";
    public bool Hidden { get; set; }
    public List<CharacteristicNode> Characteristics { get; set; } = [];
}

// A free-text rule (detachment rules, army rules); its text is in Description.
internal sealed class RuleNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Hidden { get; set; }
}

internal sealed class ConditionNode
{
    public string Type { get; set; } = "";
    public double Value { get; set; }
    public string Field { get; set; } = "";
    public string ChildId { get; set; } = "";

    // The referenced thing's name (a category, entry, ...), used only for display
    // (e.g. naming the battle size a cap modifier is gated on, or a leader-
    // attachment target when the id can't be resolved); matching is always by ChildId.
    public string ChildName { get; set; } = "";
    public string Scope { get; set; } = "";
}

// A boolean grouping of conditions ("and"/"or"), optionally nesting further
// groups. A "hidden" modifier gating a unit on a detachment/config toggle puts
// its conditions here rather than in the flat Conditions list.
internal sealed class ConditionGroupNode
{
    public string Type { get; set; } = "";
    public List<ConditionNode> Conditions { get; set; } = [];
    public List<ConditionGroupNode> ConditionGroups { get; set; } = [];
}

// An 11e leader-attachment relationship on a character entry: a "Leader" (or
// "Supported by") association naming the units it can attach to. The joinable
// units are the "instanceOf" conditions (flat or grouped) referencing a unit
// category; other conditions (wargear prerequisites, exclusions) gate the
// attachment. Min/Max/Scope mirror the association's own bounds.
internal sealed class AssociationNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Min { get; set; }
    public int Max { get; set; }
    public List<ConditionNode> Conditions { get; set; } = [];
    public List<ConditionGroupNode> ConditionGroups { get; set; } = [];
}

// A min/max bound on a selection. A model entry's squad size is a pair of these
// (field "selections", scope parent/self) rather than a range in its name.
internal sealed class ConstraintNode
{
    // The constraint's own id; a modifier targets a constraint by naming it in
    // the modifier's Field, so caps that vary by battle size can be resolved.
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Field { get; set; } = "";
    public string Scope { get; set; } = "";
    public double Value { get; set; }
}

// A rule modification; for unit sizing, a "set" on the points cost field holds
// the points for a larger model count (its condition names the threshold).
internal sealed class ModifierNode
{
    public string Type { get; set; } = "";
    public string Field { get; set; } = "";

    // Value is a number for cost modifiers, a string for name/text ones.
    public JsonElement Value { get; set; }
    public List<ConditionNode> Conditions { get; set; } = [];
    public List<ConditionGroupNode> ConditionGroups { get; set; } = [];

    // A points increment/decrement that scales with a selection count carries its
    // counter here ("+V points per N selections of a child"), rather than a flat
    // condition. Used by Crucible characters whose weapons each add points.
    public List<RepeatNode> Repeats { get; set; } = [];
}

// A modifier's repeat counter: apply the modifier Repeats times for every Value
// selections of ChildId within Scope. Value/Repeats are 1 for a plain
// per-selection modifier ("+V per weapon").
internal sealed class RepeatNode
{
    public string ChildId { get; set; } = "";
    public string Field { get; set; } = "";
    public string Scope { get; set; } = "";
    public double Value { get; set; }
    public double Repeats { get; set; }
    public bool IncludeChildSelections { get; set; }
}

internal sealed class ModifierGroupNode
{
    public List<ModifierNode> Modifiers { get; set; } = [];
    public List<ModifierGroupNode> ModifierGroups { get; set; } = [];
}

// Selection entries, groups, and links share the same child-bearing shape.
internal abstract class SelectionNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }

    // True when the node is a shared/collective pool: its children's quantities
    // are drawn from one pool spread across the parent (e.g. a weapon split
    // across a squad) rather than counted per instance. BSData sets this on the
    // group that holds distributable model variants.
    public bool Collective { get; set; }

    // Upstream display order within a container (siblings in a selectionEntries /
    // selectionEntryGroups array). Null when the source omits it, so unindexed
    // nodes can be ordered last; present throughout the 11e data. long because
    // BattleScribe sortIndex values can exceed int range.
    public long? SortIndex { get; set; }
    public List<Cost> Costs { get; set; } = [];
    public List<ConstraintNode> Constraints { get; set; } = [];
    public List<ProfileNode> Profiles { get; set; } = [];
    public List<RuleNode> Rules { get; set; } = [];
    public List<CategoryLink> CategoryLinks { get; set; } = [];
    public List<ModifierNode> Modifiers { get; set; } = [];
    public List<ModifierGroupNode> ModifierGroups { get; set; } = [];
    public List<SelectionEntryNode> SelectionEntries { get; set; } = [];
    public List<SelectionEntryGroupNode> SelectionEntryGroups { get; set; } = [];
    public List<EntryLinkNode> EntryLinks { get; set; } = [];
    public List<InfoLinkNode> InfoLinks { get; set; } = [];
    public List<InfoGroupNode> InfoGroups { get; set; } = [];
    public List<AssociationNode> Associations { get; set; } = [];
}

internal sealed class SelectionEntryNode : SelectionNode
{
    public string Type { get; set; } = "";
}

internal sealed class SelectionEntryGroupNode : SelectionNode
{
    // Names the child entry/link that is pre-selected by default when the group
    // is added (e.g. a unit's stock weapon in a "pick one" swap). Empty when the
    // group declares no default.
    public string DefaultSelectionEntryId { get; set; } = "";
}

internal sealed class EntryLinkNode : SelectionNode
{
    public string Type { get; set; } = "";
    public string TargetId { get; set; } = "";
}

// A reference to a shared profile, rule, or info group. TargetId resolves
// against the profile / rule / info-group index; Type names which, but
// resolution is by id so it is not relied on.
internal sealed class InfoLinkNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string TargetId { get; set; } = "";
    public bool Hidden { get; set; }
}

// A named bundle of profiles/rules, optionally referencing more by infoLink and
// nesting further groups.
internal sealed class InfoGroupNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<ProfileNode> Profiles { get; set; } = [];
    public List<RuleNode> Rules { get; set; } = [];
    public List<InfoLinkNode> InfoLinks { get; set; } = [];
    public List<InfoGroupNode> InfoGroups { get; set; } = [];
}
