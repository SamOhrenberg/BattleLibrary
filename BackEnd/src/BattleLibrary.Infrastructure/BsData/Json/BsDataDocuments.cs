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
internal sealed class ForceEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
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

// A min/max bound on a selection. A model entry's squad size is a pair of these
// (field "selections", scope parent/self) rather than a range in its name.
internal sealed class ConstraintNode
{
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
}

internal sealed class SelectionEntryNode : SelectionNode
{
    public string Type { get; set; } = "";
}

internal sealed class SelectionEntryGroupNode : SelectionNode;

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
