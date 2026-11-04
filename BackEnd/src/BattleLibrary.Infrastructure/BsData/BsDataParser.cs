using System.Text.Json;
using System.Text.RegularExpressions;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData.Json;

namespace BattleLibrary.Infrastructure.BsData;

/// <summary>
/// Transforms BSData (a game-system root + faction catalogues + optional shared
/// libraries) into the slim <see cref="CatalogueModel"/>. Input may be JSON
/// (11e) or BattleScribe XML (Kill Team); a <see cref="CatalogueSchema"/> names
/// the profile/cost types to key on so one extraction serves both.
/// </summary>
public sealed class BsDataParser
{
    private const int MaxDepth = 6;
    // Enhancement entries sit deeper under nested config groups than datasheets.
    private const int DetachmentMaxDepth = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // BSData serializes numeric fields (e.g. defaultCostLimit) as strings,
        // including "" for no value; this reads those leniently.
        Converters =
        {
            new Json.LenientDoubleConverter(),
            new Json.LenientIntConverter(),
            new Json.LenientStringConverter(),
        },
    };

    public GameSystemInfo ParseGameSystemInfo(string gameSystemJson)
    {
        var system = Deserialize<GameSystemRoot>(gameSystemJson).GameSystem
                     ?? throw new FormatException("Missing 'gameSystem' root.");
        return new GameSystemInfo(system.Id, system.Name, system.Revision);
    }

    public CatalogueModel ParseCatalogue(
        string gameSystemJson,
        string catalogueJson,
        IReadOnlyList<string>? libraryJsons = null,
        CatalogueSchema? schema = null)
    {
        var system = Deserialize<GameSystemRoot>(gameSystemJson).GameSystem
                     ?? throw new FormatException("Missing 'gameSystem' root.");
        var catalogue = Deserialize<CatalogueRoot>(catalogueJson).Catalogue
                        ?? throw new FormatException("Missing 'catalogue' root.");
        var libraries = (libraryJsons ?? [])
            .Select(json => Deserialize<CatalogueRoot>(json).Catalogue)
            .OfType<CatalogueDocument>()
            .ToList();

        return ParseOne(system, catalogue, libraries, schema ?? CatalogueSchema.Wh40k);
    }

    public IReadOnlyList<CatalogueModel> ParseAll(
        string gameSystemJson,
        IReadOnlyList<string> factionJsons,
        IReadOnlyList<string> libraryJsons,
        CatalogueSchema? schema = null)
    {
        var system = Deserialize<GameSystemRoot>(gameSystemJson).GameSystem
                     ?? throw new FormatException("Missing 'gameSystem' root.");
        var libraries = libraryJsons
            .Select(json => Deserialize<CatalogueRoot>(json).Catalogue)
            .OfType<CatalogueDocument>()
            .ToList();
        var factions = factionJsons
            .Select(json => Deserialize<CatalogueRoot>(json).Catalogue)
            .OfType<CatalogueDocument>()
            .ToList();

        return ParseMany(system, factions, libraries, schema ?? CatalogueSchema.Wh40k);
    }

    public IReadOnlyList<CatalogueModel> ParseAllXml(
        string gameSystemXml,
        IReadOnlyList<string> factionXmls,
        IReadOnlyList<string> libraryXmls,
        CatalogueSchema schema)
    {
        var system = BsDataXmlReader.ReadGameSystem(gameSystemXml);
        var libraries = libraryXmls.Select(BsDataXmlReader.ReadCatalogue).ToList();
        var factions = factionXmls.Select(BsDataXmlReader.ReadCatalogue).ToList();
        return ParseMany(system, factions, libraries, schema);
    }

    private static IReadOnlyList<CatalogueModel> ParseMany(
        GameSystemDocument system,
        IReadOnlyList<CatalogueDocument> factions,
        IReadOnlyList<CatalogueDocument> libraries,
        CatalogueSchema schema)
    {
        // One index and catalogue lookup across every document, so a faction that
        // imports another catalogue's root entries (a chapter importing the base
        // Space Marines catalogue) resolves those units and their references.
        var catalogues = new List<CatalogueDocument>(factions.Count + libraries.Count);
        catalogues.AddRange(factions);
        catalogues.AddRange(libraries);
        var documents = new List<DataDocument> { system };
        documents.AddRange(catalogues);

        var ptsTypeId = ResolvePointsTypeId(system, schema);
        var index = Index.Build(documents);
        var cataloguesById = BuildCatalogueLookup(catalogues);
        return factions.Select(f => BuildModel(system, f, schema, ptsTypeId, index, cataloguesById)).ToList();
    }

    private static CatalogueModel ParseOne(
        GameSystemDocument system,
        CatalogueDocument catalogue,
        IReadOnlyList<CatalogueDocument> libraries,
        CatalogueSchema schema)
    {
        var documents = new List<DataDocument> { system, catalogue };
        documents.AddRange(libraries);

        var ptsTypeId = ResolvePointsTypeId(system, schema);
        var index = Index.Build(documents);
        var cataloguesById = BuildCatalogueLookup(libraries);
        return BuildModel(system, catalogue, schema, ptsTypeId, index, cataloguesById);
    }

    private static CatalogueModel BuildModel(
        GameSystemDocument system,
        CatalogueDocument catalogue,
        CatalogueSchema schema,
        string ptsTypeId,
        Index index,
        IReadOnlyDictionary<string, CatalogueDocument> cataloguesById)
    {
        var equipment = ExtractFactionEquipment(catalogue, schema);
        var detachments = ExtractDetachments(system, catalogue, schema, ptsTypeId, cataloguesById);
        var availability = BuildAvailabilityContext(system, catalogue, detachments, index, cataloguesById, schema);
        var datasheets = ExtractDatasheets(catalogue, schema, ptsTypeId, index, cataloguesById, availability);

        var force = system.ForceEntries.FirstOrDefault();
        var limits = RosterLimitsReader.Read(system, schema, ptsTypeId);
        var info = new GameSystemInfo(
            system.Id, system.Name, system.Revision, ptsTypeId,
            force?.Id ?? "", force?.Name ?? "", limits);
        return new CatalogueModel(info, catalogue.Id, catalogue.Name, catalogue.Revision, datasheets, equipment, detachments);
    }

    // The catalogue plus every catalogue it pulls in: linked libraries (always)
    // and catalogues it imports wholesale (importRootEntries), followed
    // transitively and guarded against cycles.
    private static List<CatalogueDocument> ImportedCatalogues(
        CatalogueDocument root, IReadOnlyDictionary<string, CatalogueDocument> cataloguesById)
    {
        var result = new List<CatalogueDocument>();
        var visited = new HashSet<string>();

        void Walk(CatalogueDocument doc)
        {
            if (!string.IsNullOrEmpty(doc.Id) && !visited.Add(doc.Id))
            {
                return;
            }
            result.Add(doc);
            foreach (var link in doc.CatalogueLinks)
            {
                if (!string.IsNullOrEmpty(link.TargetId)
                    && cataloguesById.TryGetValue(link.TargetId, out var target)
                    && (target.Library || link.ImportRootEntries))
                {
                    Walk(target);
                }
            }
        }

        Walk(root);
        return result;
    }

    // Detachments: child entries of a selection group named per the schema (40k's
    // "Detachment"), each paired with its enhancements - the entries under a
    // "<Detachment> Enhancements" group that carry an enhancement counter cost,
    // snapshotting the enhancement's pts cost. Names only, no rules text.
    private static IReadOnlyList<Detachment> ExtractDetachments(
        GameSystemDocument system,
        CatalogueDocument catalogue,
        CatalogueSchema schema,
        string ptsTypeId,
        IReadOnlyDictionary<string, CatalogueDocument> cataloguesById)
    {
        if (string.IsNullOrEmpty(schema.DetachmentGroupName))
        {
            return [];
        }

        var enhCostId = system.CostTypes
            .FirstOrDefault(c => string.Equals(c.Name, schema.EnhancementCostName, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        var dpCostId = system.CostTypes
            .FirstOrDefault(c => string.Equals(c.Name, schema.DetachmentPointsCostName, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        var docs = ImportedCatalogues(catalogue, cataloguesById);

        var options = new List<(string Id, string Name, int Dp, List<Ability> Rules)>();
        var enhancements = new List<(string Det, string Id, string Name, int Points, string Text)>();
        foreach (var doc in docs)
        {
            foreach (var root in doc.SharedSelectionEntryGroups.Cast<SelectionNode>().Concat(doc.SharedSelectionEntries))
            {
                CollectDetachments(root, null, DetachmentMaxDepth, [], schema.DetachmentGroupName, enhCostId, dpCostId, ptsTypeId, catalogue.Id, schema.AbilityTypes, options, enhancements);
            }
        }

        var byDetachment = enhancements
            .GroupBy(e => e.Det, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                      .Select(e => new Enhancement(e.Id, e.Name, e.Points, e.Text))
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return options
            .DistinctBy(o => o.Id)
            .Where(o => !string.IsNullOrWhiteSpace(o.Name))
            .Select(o => new Detachment(
                o.Id, o.Name, o.Dp,
                byDetachment.TryGetValue(o.Name, out var es) ? es : [],
                o.Rules))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectDetachments(
        SelectionNode node, string? groupName, int depth, HashSet<string> visited,
        string detachmentGroupName, string enhCostId, string dpCostId, string ptsTypeId,
        string factionCatalogueId,
        IReadOnlySet<string> abilityTypes,
        List<(string Id, string Name, int Dp, List<Ability> Rules)> options,
        List<(string Det, string Id, string Name, int Points, string Text)> enhancements)
    {
        if (depth < 0 || (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id)))
        {
            return;
        }

        // Detachment options: the direct child entries of a "Detachment" group,
        // each with its Detachment Points cost (default 1 when unpriced) and its
        // detachment rules. A detachment locked to another chapter/legion is
        // skipped for this faction.
        if (node is SelectionEntryGroupNode && string.Equals(node.Name, detachmentGroupName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var child in node.SelectionEntries)
            {
                if (!string.IsNullOrEmpty(child.Id) && !HiddenForFaction(child, factionCatalogueId))
                {
                    var dp = string.IsNullOrEmpty(dpCostId)
                        ? 1
                        : child.Costs.Where(c => c.TypeId == dpCostId).Select(c => (int)Math.Round(c.Value)).DefaultIfEmpty(1).First();
                    options.Add((child.Id, child.Name, dp, CollectAbilities(child, abilityTypes, 4)));
                }
            }
        }

        // Enhancement entries: carry the enhancement counter cost; the enclosing
        // group is named "<Detachment> Enhancements". The effect text is on the
        // entry's ability profile.
        if (!string.IsNullOrEmpty(enhCostId)
            && node.Costs.Any(c => c.TypeId == enhCostId && c.Value != 0)
            && !string.IsNullOrWhiteSpace(node.Name))
        {
            var det = groupName ?? "";
            const string suffix = " Enhancements";
            if (det.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                det = det[..^suffix.Length];
            }
            var text = node.Profiles
                .Where(p => abilityTypes.Contains(p.TypeName))
                .Select(DescriptionOf)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
            enhancements.Add((det, node.Id, node.Name, PointsOf(node, ptsTypeId), text));
        }

        foreach (var child in node.SelectionEntries)
        {
            CollectDetachments(child, groupName, depth - 1, visited, detachmentGroupName, enhCostId, dpCostId, ptsTypeId, factionCatalogueId, abilityTypes, options, enhancements);
        }
        foreach (var group in node.SelectionEntryGroups)
        {
            CollectDetachments(group, group.Name, depth - 1, visited, detachmentGroupName, enhCostId, dpCostId, ptsTypeId, factionCatalogueId, abilityTypes, options, enhancements);
        }
    }

    // Faction-wide equipment (e.g. Kill Team's "Faction Equipment" group): the
    // names of catalogue-level entries carrying an equipment profile. Names only,
    // no rules text.
    private static IReadOnlyList<string> ExtractFactionEquipment(CatalogueDocument catalogue, CatalogueSchema schema)
    {
        if (schema.EquipmentTypes.Count == 0)
        {
            return [];
        }
        var names = new List<string>();
        var roots = catalogue.SharedSelectionEntries.Cast<SelectionNode>()
            .Concat(catalogue.SharedSelectionEntryGroups);
        foreach (var root in roots)
        {
            CollectEquipmentNames(root, MaxDepth, [], names, schema);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectEquipmentNames(
        SelectionNode node, int depth, HashSet<string> visited, List<string> names, CatalogueSchema schema)
    {
        if (depth < 0 || (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id)))
        {
            return;
        }
        if (node is SelectionEntryNode entry
            && entry.Profiles.Any(p => schema.EquipmentTypes.Contains(p.TypeName))
            && !string.IsNullOrWhiteSpace(entry.Name))
        {
            names.Add(entry.Name);
        }
        foreach (var child in node.SelectionEntries)
        {
            CollectEquipmentNames(child, depth - 1, visited, names, schema);
        }
        foreach (var group in node.SelectionEntryGroups)
        {
            CollectEquipmentNames(group, depth - 1, visited, names, schema);
        }
    }

    // Datasheets come from the faction's own entries, the entries its links
    // resolve to, and the entries of every catalogue it imports (libraries, and
    // catalogues imported wholesale such as a chapter's base Space Marines).
    private static List<Datasheet> ExtractDatasheets(
        CatalogueDocument faction,
        CatalogueSchema schema,
        string ptsTypeId,
        Index index,
        IReadOnlyDictionary<string, CatalogueDocument> cataloguesById,
        AvailabilityContext availability)
    {
        var candidates = new List<SelectionEntryNode>();
        foreach (var doc in ImportedCatalogues(faction, cataloguesById))
        {
            CollectCandidates(doc, candidates, index);
        }

        var pairs = candidates
            .Where(entry => !HiddenForFaction(entry, faction.Id))
            .Select(entry => (entry, sheet: ToDatasheet(entry, schema, ptsTypeId, index)))
            .Where(p => p.sheet is not null)
            .Select(p => (p.entry, sheet: p.sheet!))
            .DistinctBy(p => p.sheet.Id)
            .ToList();

        if (!availability.Enabled)
        {
            return pairs.Select(p => p.sheet).OrderBy(d => d.SortIndex).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // A library supplying the bulk of this faction's datasheets is the
        // faction's own content (Drukhari's units live in an "Aeldari" library),
        // not an allied import, so its gates don't apply here.
        var coreLibraries = ComputeCoreLibraries(pairs.Select(p => p.sheet.Id), index, availability.FactionTokens);

        return pairs
            .Select(p => p.sheet with
            {
                Availability = ComputeAvailability(p.entry, p.sheet.Id, availability, index, coreLibraries),
                ConditionalKeywords = ComputeConditionalKeywords(p.entry, availability, index),
            })
            .OrderBy(d => d.SortIndex)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Libraries that are this faction's own content rather than allied imports:
    // those supplying at least half its datasheets, plus any sharing a meaningful
    // name token with the faction.
    private static HashSet<string> ComputeCoreLibraries(
        IEnumerable<string> datasheetIds, Index index, IReadOnlySet<string> factionTokens)
    {
        var ids = datasheetIds.ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (index.EntryOrigin.TryGetValue(id, out var o) && o.IsLibrary)
            {
                counts[o.Name] = counts.GetValueOrDefault(o.Name) + 1;
            }
        }
        var total = Math.Max(1, ids.Count);
        var core = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, count) in counts)
        {
            if (count * 2 >= total || MeaningfulTokens(name).Overlaps(factionTokens))
            {
                core.Add(name);
            }
        }
        return core;
    }

    // Allied/optional content imported wholesale from a shared library (Chaos
    // Knights, Titans, Unaligned Forces, ...) carries no per-entry gate, so it is
    // gated behind a "Show <library>" toggle keyed off where it was defined.
    // Units with their own gate, core (non-library or dominant-library) units, and
    // Legends content (its own name-based filter) are left ungated.
    private static DatasheetAvailability GateByOrigin(
        string datasheetId, DatasheetAvailability current, Index index, AvailabilityContext ctx, IReadOnlySet<string> coreLibraries)
    {
        if (current.RequiredToggles.Count > 0
            || current.RequiredDetachmentIds.Count > 0
            || !index.EntryOrigin.TryGetValue(datasheetId, out var origin)
            || !origin.IsLibrary
            || coreLibraries.Contains(origin.Name))
        {
            return current;
        }
        var toggle = LibraryToggle(origin.Name, ctx.FactionTokens);
        return toggle is null ? current : new DatasheetAvailability([], [toggle]);
    }

    // "Chaos - Chaos Knights Library" -> "Show Chaos Knights"; "Library - Titans"
    // -> "Show Titans". Returns null (not gated) for a Legends library (its
    // datasheets carry a [Legends] name the builder filters on) and for the
    // faction's own content library (one sharing a meaningful name token, e.g.
    // Aeldari's units living in an "Aeldari" library).
    private static string? LibraryToggle(string libraryName, IReadOnlySet<string> factionTokens)
    {
        if (libraryName.Contains("Legends", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (MeaningfulTokens(libraryName).Overlaps(factionTokens))
        {
            return null;
        }
        var cleaned = libraryName;
        foreach (var prefix in new[] { "Chaos - ", "Library - ", "Imperium - ", "Xenos - " })
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..];
            }
        }
        cleaned = cleaned.Replace("Library", "", StringComparison.OrdinalIgnoreCase)
            .Trim().Trim('-', ':', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : $"Show {cleaned}";
    }

    private static void CollectCandidates(DataDocument document, List<SelectionEntryNode> candidates, Index index)
    {
        candidates.AddRange(document.SharedSelectionEntries);
        foreach (var link in document.EntryLinks)
        {
            if (!string.IsNullOrEmpty(link.TargetId) && index.Entries.TryGetValue(link.TargetId, out var target))
            {
                candidates.Add(target);
            }
        }
    }

    private static Dictionary<string, CatalogueDocument> BuildCatalogueLookup(IEnumerable<CatalogueDocument> catalogues)
    {
        var map = new Dictionary<string, CatalogueDocument>();
        foreach (var catalogue in catalogues)
        {
            if (!string.IsNullOrEmpty(catalogue.Id))
            {
                map.TryAdd(catalogue.Id, catalogue);
            }
        }
        return map;
    }

    private static Datasheet? ToDatasheet(SelectionEntryNode entry, CatalogueSchema schema, string ptsTypeId, Index index)
    {
        var allProfiles = new List<ProfileNode>();
        Gather(entry, MaxDepth, [], allProfiles, index, descendGroups: true);

        var unitProfiles = allProfiles.Where(p => schema.StatLineTypes.Contains(p.TypeName)).ToList();
        if (unitProfiles.Count == 0)
        {
            return null; // Not a datasheet (weapon, enhancement, stratagem, ...).
        }

        int? points = string.IsNullOrEmpty(ptsTypeId)
            ? null
            : entry.Costs
                .Where(c => c.TypeId == ptsTypeId)
                .Select(c => (int?)(int)Math.Round(c.Value))
                .FirstOrDefault();

        // The category flagged primary is the unit's battlefield role / faction
        // keyword; surface it distinctly (it also stays in the keyword list).
        var primaryKeyword = entry.CategoryLinks
            .Where(c => c.Primary && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c.Name)
            .FirstOrDefault() ?? "";

        // Keywords with the primary category first. Consumers that group a unit by
        // a single role (the UI's role sections) read that role off the front,
        // which is why the order matters here; the rest follow in source order.
        // Dedup is case-insensitive and keeps the first occurrence, so prepending
        // the primary both orders it first and removes its later duplicate.
        var keywords = Enumerable.Repeat(primaryKeyword, 1)
            .Concat(entry.CategoryLinks.Select(c => c.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyUnconditionalCategoryModifiers(entry, keywords, index);

        var weapons = allProfiles
            .Where(p => schema.WeaponTypes.Contains(p.TypeName))
            .Select(p => new WeaponProfile(p.Name, schema.RangedWeaponTypes.Contains(p.TypeName), ToCharacteristics(p)))
            .DistinctBy(w => (w.Name, w.Ranged))
            .ToList();

        var equipment = allProfiles
            .Where(p => schema.EquipmentTypes.Contains(p.TypeName))
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Innate abilities only: a group traversal would pull in enhancements
        // and Crusade battle scars, which are not part of the base datasheet.
        // Abilities come from ability-type profiles (inline or linked) and from
        // referenced rules, both resolved via infoLinks / info groups.
        var innateProfiles = new List<ProfileNode>();
        var innateRules = new List<RuleNode>();
        Gather(entry, MaxDepth, [], innateProfiles, index, descendGroups: false, innateRules);
        var abilities = innateProfiles
            .Where(p => schema.AbilityTypes.Contains(p.TypeName) && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Ability(p.Name, DescriptionOf(p), CitationFor(p, index)))
            .Concat(innateRules
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new Ability(r.Name, (r.Description ?? "").Trim(), CitationFor(r, index))))
            .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Crucible warband characters attach their Weapons / Specialisms / Abilities
        // by group link and price those weapons through selection-counting modifiers.
        // Both are handled only for this content tier (the app already treats it
        // specially), so ordinary datasheets are untouched.
        var isCrucible = entry.Name.Contains("[Crucible]", StringComparison.OrdinalIgnoreCase)
            || keywords.Any(k => string.Equals(k, "Crucible", StringComparison.OrdinalIgnoreCase));

        var options = ExtractOptions(entry, schema, ptsTypeId, index, isCrucible);
        IReadOnlyList<CostRule> costRules = isCrucible ? ExtractCostRules(entry, ptsTypeId, index, options) : [];

        var unit = unitProfiles
            .Select(p => new UnitProfile(p.Name, ToCharacteristics(p)))
            .ToList();

        // Datasheet-level source: the citation on its stat profile(s).
        var source = unitProfiles
            .Select(p => CitationFor(p, index))
            .FirstOrDefault(c => c is not null);

        var modelGroups = ExtractModelGroups(entry, schema, index);
        var sizes = ExtractSizes(entry, ptsTypeId);
        var (variantGroups, fixedModels) = ExtractVariantGroups(entry, schema, ptsTypeId, index);
        var leaderAttachments = ExtractLeaderAttachments(entry, index);

        return new Datasheet(
            entry.Id, entry.Name, points, keywords, primaryKeyword, unit, weapons, abilities, options, equipment, sizes,
            modelGroups, variantGroups, fixedModels, DatasheetAvailability.None, leaderAttachments, costRules,
            entry.SortIndex ?? long.MaxValue, source);
    }

    // Leader attachment (11e): a character's "associations" name the units it can
    // join. The joinable units are the association's "instanceOf" conditions that
    // reference a unit category (the 11e datasheet-category mechanic); conditions
    // referencing an entry (a wargear/enhancement prerequisite) or using other
    // types (e.g. "lessThan" exclusions) gate the attachment and are not targets.
    // Target names come from the category index, falling back to the condition's
    // own ChildName.
    private static IReadOnlyList<LeaderAttachment> ExtractLeaderAttachments(SelectionEntryNode entry, Index index)
    {
        var result = new List<LeaderAttachment>();
        foreach (var association in entry.Associations)
        {
            var targets = AllConditions(association)
                .Where(c => string.Equals(c.Type, "instanceOf", StringComparison.OrdinalIgnoreCase)
                            && index.Categories.ContainsKey(c.ChildId))
                .Select(c => index.Categories.TryGetValue(c.ChildId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : c.ChildName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count > 0)
            {
                var label = string.IsNullOrWhiteSpace(association.Label) ? "Leader" : association.Label;
                result.Add(new LeaderAttachment(label, targets));
            }
        }
        return result;
    }

    // An association's conditions, flattening its nested AND/OR condition groups.
    // Unlike the "hidden" modifier flattening, OR groups are kept (a leader's
    // targets are a union across OR branches), since we collect targets, not a
    // single boolean gate.
    private static IEnumerable<ConditionNode> AllConditions(AssociationNode association)
    {
        foreach (var c in association.Conditions)
        {
            yield return c;
        }
        foreach (var c in AllConditionsInGroups(association.ConditionGroups))
        {
            yield return c;
        }
    }

    private static IEnumerable<ConditionNode> AllConditionsInGroups(IEnumerable<ConditionGroupNode> groups)
    {
        foreach (var g in groups)
        {
            foreach (var c in g.Conditions)
            {
                yield return c;
            }
            foreach (var c in AllConditionsInGroups(g.ConditionGroups))
            {
                yield return c;
            }
        }
    }

    // Distributable model pools: a selection group holding more than one model
    // variant (e.g. Rubric Marines split across boltgun / warpflamer / soulreaper)
    // the player splits across loadouts. Also returns the fixed model count: the
    // always-present single models (the Aspiring Sorcerer), used to derive the
    // pool target for a chosen size (poolTarget = size - fixedCount).
    private static (IReadOnlyList<ModelVariantGroup> Groups, int FixedCount) ExtractVariantGroups(
        SelectionEntryNode entry, CatalogueSchema schema, string ptsTypeId, Index index)
    {
        var groups = new List<ModelVariantGroup>();
        var fixedCount = 0;

        // Direct single models with a fixed count are the unit's fixed models.
        foreach (var s in entry.SelectionEntries)
        {
            if (!IsModel(s.Type))
            {
                continue;
            }
            var (lo, hi) = CountFromConstraints(s);
            if (lo == hi)
            {
                fixedCount += lo;
            }
        }

        // A group of model variants the player distributes across one shared pool.
        // BSData marks such a pool with collective="true", so honor that flag
        // rather than guessing from shape: a collective group is a pool even when
        // it holds a single model variant. For data that omits the flag we fall
        // back to the old heuristic (more than one variant => pool). A non-collective
        // group with a single fixed-size model is just another fixed model, and a
        // single model with a size range is the unit's size (ExtractSizes), not a pool.
        foreach (var g in entry.SelectionEntryGroups)
        {
            var models = g.SelectionEntries.Where(m => IsModel(m.Type)).ToList();
            if (models.Count == 0)
            {
                continue;
            }
            var (lo, hi) = GroupModelCount(g, models);
            if (g.Collective || models.Count > 1)
            {
                var variants = models.Select(m => ToVariant(m, g, schema, ptsTypeId, index)).ToList();
                groups.Add(new ModelVariantGroup(g.Id, g.Name, hi, variants));
            }
            else if (lo == hi)
            {
                fixedCount += lo;
            }
        }

        return (groups, fixedCount);
    }

    private static ModelVariant ToVariant(SelectionEntryNode model, SelectionEntryGroupNode group, CatalogueSchema schema, string ptsTypeId, Index index)
    {
        var (_, hi) = CountFromConstraints(model);
        return new ModelVariant(model.Id, model.Name, hi, PointsOf(model, ptsTypeId), OptionWeapons(model, schema, index), IsGroupDefault(group, model));
    }

    // Whether a group's child (a selection entry or entry link) is the one named
    // by the group's defaultSelectionEntryId - matched on the child id, or on an
    // entry link's target id, since a group may name either.
    private static bool IsGroupDefault(SelectionEntryGroupNode group, SelectionNode choice)
    {
        if (string.IsNullOrEmpty(group.DefaultSelectionEntryId))
        {
            return false;
        }
        return choice.Id == group.DefaultSelectionEntryId
            || (choice is EntryLinkNode link && link.TargetId == group.DefaultSelectionEntryId);
    }

    // Collects profiles from a node, its children, and any entries/profiles/rules
    // its links resolve to, across the game system, catalogue, and libraries. When
    // a rules list is supplied, rule references (and info-group rules) land there;
    // profiles always land in profiles. Hidden info references are skipped.
    private static void Gather(
        SelectionNode node, int depth, HashSet<string> visited, List<ProfileNode> profiles,
        Index index, bool descendGroups, List<RuleNode>? rules = null)
    {
        if (depth < 0)
        {
            return;
        }
        if (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id))
        {
            return;
        }

        profiles.AddRange(node.Profiles);
        rules?.AddRange(node.Rules.Where(r => !r.Hidden));

        foreach (var info in node.InfoLinks)
        {
            ResolveInfoLink(info, visited, profiles, rules, index);
        }
        foreach (var group in node.InfoGroups)
        {
            ExpandInfoGroup(group, visited, profiles, rules, index);
        }

        if (node is EntryLinkNode link && !string.IsNullOrEmpty(link.TargetId))
        {
            if (index.Profiles.TryGetValue(link.TargetId, out var profile))
            {
                profiles.Add(profile);
            }
            else if (index.Entries.TryGetValue(link.TargetId, out var entry))
            {
                Gather(entry, depth - 1, visited, profiles, index, descendGroups, rules);
            }
            else if (index.Groups.TryGetValue(link.TargetId, out var group))
            {
                Gather(group, depth - 1, visited, profiles, index, descendGroups, rules);
            }
        }

        foreach (var child in node.SelectionEntries)
        {
            Gather(child, depth - 1, visited, profiles, index, descendGroups, rules);
        }

        if (descendGroups)
        {
            foreach (var group in node.SelectionEntryGroups)
            {
                Gather(group, depth - 1, visited, profiles, index, descendGroups, rules);
            }
        }

        foreach (var childLink in node.EntryLinks)
        {
            Gather(childLink, depth - 1, visited, profiles, index, descendGroups, rules);
        }
    }

    // Resolves an infoLink's target against the profile / rule / info-group index.
    // Rules are collected only when a rules list is supplied.
    private static void ResolveInfoLink(
        InfoLinkNode info, HashSet<string> visited, List<ProfileNode> profiles, List<RuleNode>? rules, Index index)
    {
        if (info.Hidden || string.IsNullOrEmpty(info.TargetId))
        {
            return;
        }
        if (index.Profiles.TryGetValue(info.TargetId, out var profile))
        {
            profiles.Add(profile);
        }
        else if (index.Rules.TryGetValue(info.TargetId, out var rule))
        {
            if (rules is not null && !rule.Hidden) rules.Add(rule);
        }
        else if (index.InfoGroups.TryGetValue(info.TargetId, out var group))
        {
            ExpandInfoGroup(group, visited, profiles, rules, index);
        }
    }

    // Expands an info group into its profiles/rules, following its own links and
    // nested groups. Guarded against cycles via the shared visited set.
    private static void ExpandInfoGroup(
        InfoGroupNode group, HashSet<string> visited, List<ProfileNode> profiles, List<RuleNode>? rules, Index index)
    {
        if (group.Hidden)
        {
            return;
        }
        if (!string.IsNullOrEmpty(group.Id) && !visited.Add(group.Id))
        {
            return;
        }
        profiles.AddRange(group.Profiles);
        rules?.AddRange(group.Rules.Where(r => !r.Hidden));
        foreach (var info in group.InfoLinks)
        {
            ResolveInfoLink(info, visited, profiles, rules, index);
        }
        foreach (var nested in group.InfoGroups)
        {
            ExpandInfoGroup(nested, visited, profiles, rules, index);
        }
    }

    // Wargear/upgrade choices the datasheet offers: the children of the unit's
    // selection groups, with their additive pts cost (0 when free or point-less).
    // Model/unit children are size brackets, not add-ons, so they are skipped.
    // resolveGroupLinks follows entryLinks that point at a shared selection group
    // (not just an entry). It is enabled only for Crucible datasheets, whose
    // Weapons / Specialisms / Abilities choices are attached solely by group link;
    // enabling it everywhere would restructure options for nearly every datasheet.
    private static IReadOnlyList<DatasheetOption> ExtractOptions(
        SelectionEntryNode entry, CatalogueSchema schema, string ptsTypeId, Index index, bool resolveGroupLinks)
    {
        var options = new List<DatasheetOption>();
        CollectOptions(entry, "", MaxDepth, [], options, index, ptsTypeId, schema, resolveGroupLinks);
        return options
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && !string.IsNullOrEmpty(o.Id))
            .DistinctBy(o => o.Id)
            .ToList();
    }

    // Weapon stat line(s) a wargear choice grants, for the option preview.
    private static IReadOnlyList<WeaponProfile> OptionWeapons(SelectionNode choice, CatalogueSchema schema, Index index)
    {
        var profiles = new List<ProfileNode>();
        Gather(choice, 2, [], profiles, index, descendGroups: false);
        return profiles
            .Where(p => schema.WeaponTypes.Contains(p.TypeName))
            .Select(p => new WeaponProfile(p.Name, schema.RangedWeaponTypes.Contains(p.TypeName), ToCharacteristics(p)))
            .DistinctBy(w => (w.Name, w.Ranged))
            .ToList();
    }

    // Model components of a unit: model-type entries that are direct children or
    // sit in a selection group (a squad lists each model type separately). The
    // group name, when present, carries the count range for older-style entries.
    private static IEnumerable<(SelectionNode Model, string? GroupName)> ModelComponents(SelectionEntryNode entry)
    {
        foreach (var s in entry.SelectionEntries)
        {
            if (IsModel(s.Type))
            {
                yield return (s, null);
            }
        }
        foreach (var g in entry.SelectionEntryGroups)
        {
            foreach (var s in g.SelectionEntries)
            {
                if (IsModel(s.Type))
                {
                    yield return (s, g.Name);
                }
            }
        }
    }

    // Model composition: each model type with its default weapons. Empty for
    // single-model units (the weapons table suffices).
    private static IReadOnlyList<ModelLoadout> ExtractModelGroups(SelectionEntryNode entry, CatalogueSchema schema, Index index)
    {
        var groups = new List<ModelLoadout>();
        foreach (var (model, groupName) in ModelComponents(entry))
        {
            var label = !string.IsNullOrWhiteSpace(groupName) ? groupName! : model.Name;
            groups.Add(new ModelLoadout(label, ModelWeapons(model, schema, index)));
        }
        return groups;
    }

    private static IReadOnlyList<string> ModelWeapons(SelectionNode model, CatalogueSchema schema, Index index)
    {
        var profiles = new List<ProfileNode>();
        Gather(model, 2, [], profiles, index, descendGroups: false);
        return profiles
            .Where(p => schema.WeaponTypes.Contains(p.TypeName) && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Selectable sizes: the smallest size at the base cost, and (when the unit
    // scales) the largest size at the highest "set" points modifier. Model totals
    // come from the model-group name ranges (e.g. "9-18" + "1-2" -> 10..20).
    private static IReadOnlyList<UnitSize> ExtractSizes(SelectionEntryNode entry, string ptsTypeId)
    {
        var basePts = PointsOf(entry, ptsTypeId);
        var (minTotal, maxTotal) = ModelTotals(entry);
        var sizes = new List<UnitSize> { new(Math.Max(1, minTotal), basePts) };
        if (maxTotal > minTotal)
        {
            var setVals = CostSetValues(entry, ptsTypeId);
            var largePts = setVals.Count > 0 ? setVals.Max() : basePts;
            sizes.Add(new UnitSize(maxTotal, largePts));
        }
        return sizes.DistinctBy(s => s.Models).ToList();
    }

    private static (int Min, int Max) ModelTotals(SelectionEntryNode entry)
    {
        int min = 0, max = 0;
        var any = false;

        // Models listed directly on the entry carry their count in constraints.
        foreach (var s in entry.SelectionEntries)
        {
            if (!IsModel(s.Type))
            {
                continue;
            }
            var (lo, hi) = CountFromConstraints(s);
            min += lo;
            max += hi;
            any = true;
        }

        // A group of model variants (e.g. the same Rubric Marine with different
        // weapons) shares one model pool, so its range counts once for the group,
        // not once per variant.
        foreach (var g in entry.SelectionEntryGroups)
        {
            var models = g.SelectionEntries.Where(s => IsModel(s.Type)).ToList();
            if (models.Count == 0)
            {
                continue;
            }
            var (lo, hi) = GroupModelCount(g, models);
            min += lo;
            max += hi;
            any = true;
        }

        return any ? (min, max) : (1, 1);
    }

    // A grouped model pool's size. The group name, when it spells a range (e.g.
    // "9-18 Boyz"), is the datasheet's intended size and wins; then the group's own
    // "selections" constraints; otherwise the members' counts summed.
    private static (int Lo, int Hi) GroupModelCount(SelectionEntryGroupNode group, IReadOnlyList<SelectionEntryNode> models)
    {
        if (RangeFromName(group.Name) is { } named)
        {
            return named;
        }
        var (gmin, gmax) = ConstraintRange(group);
        if (gmin.HasValue || gmax.HasValue)
        {
            return (gmin ?? gmax ?? 1, gmax ?? gmin ?? 1);
        }
        int min = 0, max = 0;
        foreach (var s in models)
        {
            var (lo, hi) = CountFromConstraints(s);
            min += lo;
            max += hi;
        }
        return (min, max);
    }

    // A model's count from its "selections" min/max constraints (scope
    // parent/self), defaulting to a single model when none are set.
    private static (int Lo, int Hi) CountFromConstraints(SelectionNode model)
    {
        var (min, max) = ConstraintRange(model);
        return (min.HasValue || max.HasValue) ? (min ?? max ?? 1, max ?? min ?? 1) : (1, 1);
    }

    private static (int? Min, int? Max) ConstraintRange(SelectionNode node)
    {
        int? min = null, max = null;
        foreach (var c in node.Constraints)
        {
            if (c.Field != "selections" || (c.Scope != "parent" && c.Scope != "self"))
            {
                continue;
            }
            if (c.Type == "min")
            {
                min = (int)c.Value;
            }
            else if (c.Type == "max")
            {
                max = (int)c.Value;
            }
        }
        return (min, max);
    }

    private static readonly Regex RangeRx = new(@"(\d+)\s*-\s*(\d+)|(\d+)", RegexOptions.Compiled);

    // A count range spelled in a group name ("9-18 Boyz", "Gore Hound"); null when
    // the name carries no number, so counting falls through to the constraints.
    private static (int Lo, int Hi)? RangeFromName(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var m = RangeRx.Match(text);
        if (!m.Success)
        {
            return null;
        }
        if (m.Groups[1].Success)
        {
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        }
        var n = int.Parse(m.Groups[3].Value);
        return (n, n);
    }

    // A shared entry imported into a faction can be locked to a different
    // catalogue (a detachment or unit for another chapter/legion). The lock is a
    // "hidden" modifier set true whose conditions are primary-catalogue membership
    // tests; it hides the entry for every faction those tests exclude.
    private static bool HiddenForFaction(SelectionNode node, string factionCatalogueId)
    {
        foreach (var m in node.Modifiers)
        {
            if (IsFactionLockHidden(m, factionCatalogueId))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsFactionLockHidden(ModifierNode m, string factionCatalogueId)
    {
        if (!string.Equals(m.Type, "set", StringComparison.OrdinalIgnoreCase)
            || m.Field != "hidden"
            || m.Value.ValueKind != JsonValueKind.True
            || m.Conditions.Count == 0)
        {
            return false;
        }
        // Every condition must be a primary-catalogue membership test; anything
        // else (roster/selection counts, battle size) is dynamic, not a lock.
        var hide = true;
        foreach (var c in m.Conditions)
        {
            if (!string.Equals(c.Scope, "primary-catalogue", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var isThisFaction = c.ChildId == factionCatalogueId;
            if (string.Equals(c.Type, "notInstanceOf", StringComparison.OrdinalIgnoreCase))
            {
                hide &= !isThisFaction;
            }
            else if (string.Equals(c.Type, "instanceOf", StringComparison.OrdinalIgnoreCase))
            {
                hide &= isThisFaction;
            }
            else
            {
                return false;
            }
        }
        return hide;
    }

    // Resolved ids and names used to classify a datasheet's "hidden" gates.
    private sealed record AvailabilityContext(
        IReadOnlySet<string> DetachmentIds,
        IReadOnlySet<string> CrusadeForceIds,
        IReadOnlyDictionary<string, string> IdToName,
        IReadOnlyDictionary<string, CatalogueOrigin> EntryOrigin,
        IReadOnlySet<string> FactionTokens,
        bool Enabled);

    private static AvailabilityContext BuildAvailabilityContext(
        GameSystemDocument system,
        CatalogueDocument catalogue,
        IReadOnlyList<Detachment> detachments,
        Index index,
        IReadOnlyDictionary<string, CatalogueDocument> cataloguesById,
        CatalogueSchema schema)
    {
        // Only 40k-style systems carry detachments + "Show X" gates; Kill Team has none.
        if (string.IsNullOrEmpty(schema.DetachmentGroupName))
        {
            return new AvailabilityContext(new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, CatalogueOrigin>(), new HashSet<string>(), false);
        }

        var docs = new List<DataDocument> { system };
        docs.AddRange(ImportedCatalogues(catalogue, cataloguesById));

        var idToName = new Dictionary<string, string>();
        void Add(string id, string name)
        {
            if (!string.IsNullOrEmpty(id) && !idToName.ContainsKey(id)) idToName[id] = name;
        }
        foreach (var (id, e) in index.Entries) Add(id, e.Name);
        foreach (var (id, g) in index.Groups) Add(id, g.Name);
        var crusadeForceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var c in doc.CategoryEntries) Add(c.Id, c.Name);
            foreach (var f in doc.ForceEntries)
            {
                Add(f.Id, f.Name);
                if (f.Name.Contains("Crusade", StringComparison.OrdinalIgnoreCase)) crusadeForceIds.Add(f.Id);
            }
        }

        return new AvailabilityContext(
            detachments.Select(d => d.Id).ToHashSet(StringComparer.Ordinal),
            crusadeForceIds, idToName, index.EntryOrigin, MeaningfulTokens(catalogue.Name), true);
    }

    // Name words that distinguish a faction/library, dropping the family prefix
    // and generic words so "Thousand Sons" vs "Chaos Knights Library" don't both
    // collapse to "Chaos". Used to tell a faction's own content library from an
    // allied one.
    private static readonly HashSet<string> TokenStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "chaos", "imperium", "xenos", "adeptus", "astartes", "library", "libraries",
        "forces", "force", "the", "of", "and", "legends",
    };

    private static HashSet<string> MeaningfulTokens(string name) =>
        name.Split([' ', '-', ':', ',', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3 && !TokenStopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // A datasheet's conditional visibility, read from its "hidden" set-true
    // modifiers (matched-play assumption: the force is never a Crusade force).
    // A unit is hidden when ANY such modifier fires, and a modifier fires when
    // ALL its conditions hold; the parser records only the gates it can model
    // with confidence (a single config toggle, or a required detachment) and
    // leaves anything dynamic visible. Finally, wholesale library imports that
    // carry no gate are gated by their origin library.
    private static DatasheetAvailability ComputeAvailability(
        SelectionEntryNode entry, string datasheetId, AvailabilityContext ctx, Index index, IReadOnlySet<string> coreLibraries)
    {
        var requiredDetachments = new List<string>();
        var requiredToggles = new List<string>();

        foreach (var modifier in HiddenSetTrueModifiers(entry))
        {
            if (!TryFlattenAnd(modifier, out var conditions))
            {
                continue; // Contains an OR branch - can't model, leave visible.
            }

            var detReqs = new List<string>();
            var toggleReqs = new List<string>();
            var skip = false;
            foreach (var c in conditions)
            {
                switch (Classify(c, ctx, coreLibraries))
                {
                    case GateKind.AlwaysTrue:
                        continue; // e.g. "force is not a Crusade force" in matched play.
                    case GateKind.NeverFires:
                    case GateKind.Dynamic:
                    case GateKind.ExternallyHandled:
                        skip = true;
                        break;
                    case GateKind.Detachment:
                        detReqs.Add(c.ChildId);
                        continue;
                    case GateKind.Toggle:
                        toggleReqs.Add(ctx.IdToName[c.ChildId]);
                        continue;
                }
                if (skip) break;
            }
            if (skip)
            {
                continue;
            }

            // Hide fires only when every condition holds, so showing the unit means
            // falsifying one controllable condition. With exactly one, that gate is
            // the requirement; with several it is an OR this flat model can't carry,
            // so leave it visible.
            var controllable = detReqs.Count + toggleReqs.Count;
            if (controllable == 1)
            {
                if (detReqs.Count == 1) requiredDetachments.Add(detReqs[0]);
                else requiredToggles.Add(toggleReqs[0]);
            }
        }

        var availability = requiredDetachments.Count == 0 && requiredToggles.Count == 0
            ? DatasheetAvailability.None
            : new DatasheetAvailability(
                requiredDetachments.Distinct().ToList(),
                requiredToggles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        return GateByOrigin(datasheetId, availability, index, ctx, coreLibraries);
    }

    private enum GateKind { Dynamic, AlwaysTrue, NeverFires, Detachment, Toggle, ExternallyHandled }

    // What a single hide-condition means for matched-play visibility.
    private static GateKind Classify(ConditionNode c, AvailabilityContext ctx, IReadOnlySet<string> coreLibraries)
    {
        if (ctx.CrusadeForceIds.Contains(c.ChildId))
        {
            // Matched play is never a Crusade force.
            if (IsAbsentTest(c)) return GateKind.AlwaysTrue;  // "not a Crusade force" holds.
            if (IsPresentTest(c)) return GateKind.NeverFires;  // "is a Crusade force" can't.
            return GateKind.Dynamic;
        }
        if (ctx.DetachmentIds.Contains(c.ChildId))
        {
            return IsAbsentTest(c) ? GateKind.Detachment : GateKind.Dynamic;
        }
        if (ctx.IdToName.TryGetValue(c.ChildId, out var name) && name.StartsWith("Show ", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsAbsentTest(c)) return GateKind.Dynamic;
            // Legends / Crucible already have their own name-based builder filters.
            if (name.Contains("Legends", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Crucible", StringComparison.OrdinalIgnoreCase))
            {
                return GateKind.ExternallyHandled;
            }
            // A toggle defined in this faction's own content library is core here,
            // not an opt-in (daemons are core to Chaos Daemons, allied to others).
            if (ctx.EntryOrigin.TryGetValue(c.ChildId, out var origin) && coreLibraries.Contains(origin.Name))
            {
                return GateKind.ExternallyHandled;
            }
            return GateKind.Toggle;
        }
        return GateKind.Dynamic;
    }

    // Condition holds when the gated thing is NOT in the roster (so its presence
    // is what shows the unit): "<1 selected", "not an instance of", "at most 0".
    private static bool IsAbsentTest(ConditionNode c) =>
        (string.Equals(c.Type, "lessThan", StringComparison.OrdinalIgnoreCase) && c.Value <= 1)
        || (string.Equals(c.Type, "atMost", StringComparison.OrdinalIgnoreCase) && c.Value <= 0)
        || (string.Equals(c.Type, "equalTo", StringComparison.OrdinalIgnoreCase) && c.Value == 0)
        || string.Equals(c.Type, "notInstanceOf", StringComparison.OrdinalIgnoreCase);

    private static bool IsPresentTest(ConditionNode c) =>
        string.Equals(c.Type, "instanceOf", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(c.Type, "atLeast", StringComparison.OrdinalIgnoreCase) && c.Value >= 1)
        || (string.Equals(c.Type, "greaterThan", StringComparison.OrdinalIgnoreCase) && c.Value >= 0)
        || (string.Equals(c.Type, "equalTo", StringComparison.OrdinalIgnoreCase) && c.Value >= 1);

    // "hidden" set-true modifiers on the entry and its nested "and" modifier groups.
    private static IEnumerable<ModifierNode> HiddenSetTrueModifiers(SelectionEntryNode entry)
    {
        foreach (var m in entry.Modifiers)
        {
            if (IsHiddenSetTrue(m)) yield return m;
        }
        foreach (var m in FlattenModifierGroups(entry.ModifierGroups))
        {
            if (IsHiddenSetTrue(m)) yield return m;
        }
    }

    private static IEnumerable<ModifierNode> FlattenModifierGroups(IEnumerable<ModifierGroupNode> groups)
    {
        foreach (var g in groups)
        {
            foreach (var m in g.Modifiers) yield return m;
            foreach (var m in FlattenModifierGroups(g.ModifierGroups)) yield return m;
        }
    }

    private static bool IsHiddenSetTrue(ModifierNode m) =>
        string.Equals(m.Type, "set", StringComparison.OrdinalIgnoreCase)
        && m.Field == "hidden"
        && m.Value.ValueKind == JsonValueKind.True;

    // Flattens a modifier's conditions and nested "and" groups into one AND list.
    // Fails (returns false) on any "or" group, which this model can't represent.
    private static bool TryFlattenAnd(ModifierNode m, out List<ConditionNode> conditions)
    {
        conditions = [..m.Conditions];
        return FlattenGroups(m.ConditionGroups, conditions);
    }

    private static bool FlattenGroups(IEnumerable<ConditionGroupNode> groups, List<ConditionNode> into)
    {
        foreach (var g in groups)
        {
            if (!string.Equals(g.Type, "and", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            into.AddRange(g.Conditions);
            if (!FlattenGroups(g.ConditionGroups, into))
            {
                return false;
            }
        }
        return true;
    }

    // Values of "set" modifiers targeting the points cost - the price at a larger
    // model count. Unconditional increments are ignored: the base cost is the
    // authoritative price for the smallest size.
    private static List<int> CostSetValues(SelectionEntryNode entry, string ptsTypeId)
    {
        var vals = new List<int>();
        void Collect(IEnumerable<ModifierNode> mods)
        {
            foreach (var m in mods)
            {
                if (string.Equals(m.Type, "set", StringComparison.OrdinalIgnoreCase)
                    && m.Field == ptsTypeId
                    && m.Value.ValueKind == JsonValueKind.Number)
                {
                    vals.Add((int)Math.Round(m.Value.GetDouble()));
                }
            }
        }
        void Walk(IEnumerable<ModifierGroupNode> groups)
        {
            foreach (var g in groups)
            {
                Collect(g.Modifiers);
                Walk(g.ModifierGroups);
            }
        }
        Collect(entry.Modifiers);
        Walk(entry.ModifierGroups);
        return vals;
    }

    // Selection-dependent points rules for a Crucible character: increment /
    // decrement modifiers on the points cost that scale with, or are gated on, how
    // many choices are taken in a counted group (its weapons). A per-selection
    // modifier (repeats) becomes a PerSelection rule; a conditional one
    // (atLeast/atMost/... selections) becomes a threshold rule. Each is resolved
    // to the datasheet option ids it counts so pricing needs no group lookup later.
    private static IReadOnlyList<CostRule> ExtractCostRules(
        SelectionEntryNode entry, string ptsTypeId, Index index, IReadOnlyList<DatasheetOption> options)
    {
        if (string.IsNullOrEmpty(ptsTypeId))
        {
            return [];
        }
        var surfaced = options.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var rules = new List<CostRule>();

        void Consider(ModifierNode m)
        {
            var sign = string.Equals(m.Type, "increment", StringComparison.OrdinalIgnoreCase) ? 1
                : string.Equals(m.Type, "decrement", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
            if (sign == 0 || m.Field != ptsTypeId || m.Value.ValueKind != JsonValueKind.Number)
            {
                return;
            }
            var amount = sign * (int)Math.Round(m.Value.GetDouble());

            // "+amount points for every Value selections of the counted group."
            foreach (var r in m.Repeats)
            {
                var counted = CountedOptionIds(r.ChildId, index, surfaced);
                if (counted.Count == 0)
                {
                    continue;
                }
                var step = Math.Max(1, (int)Math.Round(r.Value));
                var times = Math.Max(1, (int)Math.Round(r.Repeats));
                rules.Add(new CostRule(counted, amount * times, PerSelection: true, step, "", 0));
            }

            // "+amount points when (count OP Value) selections of the counted group."
            foreach (var c in m.Conditions)
            {
                if (!string.Equals(c.Field, "selections", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (NormalizeOperator(c.Type) is not { } op)
                {
                    continue;
                }
                var counted = CountedOptionIds(c.ChildId, index, surfaced);
                if (counted.Count == 0)
                {
                    continue;
                }
                rules.Add(new CostRule(counted, amount, PerSelection: false, 0, op, (int)Math.Round(c.Value)));
            }
        }

        foreach (var m in entry.Modifiers)
        {
            Consider(m);
        }
        foreach (var m in FlattenModifierGroups(entry.ModifierGroups))
        {
            Consider(m);
        }
        return rules;
    }

    private static string? NormalizeOperator(string type) => (type ?? "").ToLowerInvariant() switch
    {
        "atleast" => "atLeast",
        "greaterthan" => "greaterThan",
        "atmost" => "atMost",
        "lessthan" => "lessThan",
        "equalto" => "equalTo",
        _ => null,
    };

    // The surfaced option ids that fall under a counted group (by id). Walks the
    // group's descendants and keeps only ids that were actually surfaced as
    // options, so counting a rule means counting selected options with these ids.
    private static IReadOnlyList<string> CountedOptionIds(string groupId, Index index, HashSet<string> surfaced)
    {
        if (string.IsNullOrEmpty(groupId) || !index.Groups.TryGetValue(groupId, out var group))
        {
            return [];
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectDescendantIds(group, index, [], ids);
        ids.IntersectWith(surfaced);
        return ids.Count == 0 ? [] : ids.ToList();
    }

    private static void CollectDescendantIds(SelectionNode node, Index index, HashSet<string> visited, HashSet<string> ids)
    {
        if (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id))
        {
            return;
        }

        void AddLink(EntryLinkNode l)
        {
            if (string.IsNullOrEmpty(l.TargetId))
            {
                return;
            }
            ids.Add(!string.IsNullOrEmpty(l.Id) ? l.Id : l.TargetId);
            ids.Add(l.TargetId);
            if (index.Entries.TryGetValue(l.TargetId, out var te))
            {
                CollectDescendantIds(te, index, visited, ids);
            }
            else if (index.Groups.TryGetValue(l.TargetId, out var tg))
            {
                CollectDescendantIds(tg, index, visited, ids);
            }
        }

        foreach (var g in node.SelectionEntryGroups)
        {
            CollectDescendantIds(g, index, visited, ids);
        }
        foreach (var e in node.SelectionEntries)
        {
            if (!string.IsNullOrEmpty(e.Id))
            {
                ids.Add(e.Id);
            }
            CollectDescendantIds(e, index, visited, ids);
        }
        foreach (var l in node.EntryLinks)
        {
            AddLink(l);
        }
    }

    // "category" modifiers that always apply (no conditions) change the unit's
    // fixed keyword set: add / set / set-primary grant a keyword, remove strips
    // one. Only entry-level modifiers are folded here - a category modifier nested
    // in a modifier group may be gated by a group condition this model doesn't
    // carry, so it is left to ComputeConditionalKeywords (via its own conditions)
    // rather than risk granting a keyword that should be conditional.
    private static void ApplyUnconditionalCategoryModifiers(
        SelectionEntryNode entry, List<string> keywords, Index index)
    {
        foreach (var m in entry.Modifiers)
        {
            if (!IsCategoryModifier(m) || m.Conditions.Count > 0 || m.ConditionGroups.Count > 0)
            {
                continue;
            }
            var name = CategoryName(m, index, entry);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (IsCategoryRemove(m))
            {
                keywords.RemoveAll(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            }
            else if (IsCategoryAdd(m) && !keywords.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
            {
                keywords.Add(name);
            }
        }
    }

    // Conditional "category" modifiers: a keyword gained or stripped only when a
    // gate holds (e.g. a unit gaining Battleline under the War Horde detachment).
    // Recorded only when the gate is a single "present" test on a known detachment
    // or a "Show X" config toggle - the same shapes DatasheetAvailability models -
    // so the builder can apply the change for the active detachment/toggle.
    private static IReadOnlyList<ConditionalKeyword> ComputeConditionalKeywords(
        SelectionEntryNode entry, AvailabilityContext ctx, Index index)
    {
        var result = new List<ConditionalKeyword>();
        foreach (var m in CategoryModifiers(entry))
        {
            // Unconditional ones are already folded into the static keyword set.
            if (m.Conditions.Count == 0 && m.ConditionGroups.Count == 0)
            {
                continue;
            }
            var add = IsCategoryAdd(m);
            if (!add && !IsCategoryRemove(m))
            {
                continue; // e.g. unset-primary: no change to the keyword set.
            }
            var name = CategoryName(m, index, entry);
            if (string.IsNullOrWhiteSpace(name) || !TryFlattenAnd(m, out var conditions) || conditions.Count == 0)
            {
                continue; // Unresolvable, or an OR branch this flat model can't carry.
            }

            var detReqs = new List<string>();
            var toggleReqs = new List<string>();
            var ok = true;
            foreach (var c in conditions)
            {
                // The change fires when the gated thing IS present (detachment
                // selected / toggle on), so only present-tests are modelled.
                if (!IsPresentTest(c))
                {
                    ok = false;
                    break;
                }
                if (ctx.DetachmentIds.Contains(c.ChildId))
                {
                    detReqs.Add(c.ChildId);
                }
                else if (ctx.IdToName.TryGetValue(c.ChildId, out var tn)
                         && tn.StartsWith("Show ", StringComparison.OrdinalIgnoreCase))
                {
                    toggleReqs.Add(tn);
                }
                else
                {
                    ok = false; // A dynamic condition (roster counts, battle size, ...).
                    break;
                }
            }
            // One gate is a clean "when X is selected" requirement; an AND of
            // several can't be carried by this flat model, so skip it.
            if (!ok || detReqs.Count + toggleReqs.Count != 1)
            {
                continue;
            }
            result.Add(new ConditionalKeyword(name, add, detReqs, toggleReqs));
        }

        return result
            .DistinctBy(k => (
                k.Keyword.ToLowerInvariant(),
                k.Add,
                string.Join(",", k.RequiredDetachmentIds),
                string.Join(",", k.RequiredToggles)))
            .ToList();
    }

    // "category" modifiers on the entry and its nested modifier groups.
    private static IEnumerable<ModifierNode> CategoryModifiers(SelectionEntryNode entry)
    {
        foreach (var m in entry.Modifiers)
        {
            if (IsCategoryModifier(m)) yield return m;
        }
        foreach (var m in FlattenModifierGroups(entry.ModifierGroups))
        {
            if (IsCategoryModifier(m)) yield return m;
        }
    }

    // A modifier targeting the "category" field, whose value is the id of the
    // category (keyword) to change.
    private static bool IsCategoryModifier(ModifierNode m) =>
        string.Equals(m.Field, "category", StringComparison.OrdinalIgnoreCase)
        && m.Value.ValueKind == JsonValueKind.String;

    private static bool IsCategoryAdd(ModifierNode m) =>
        string.Equals(m.Type, "add", StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.Type, "set", StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.Type, "set-primary", StringComparison.OrdinalIgnoreCase);

    private static bool IsCategoryRemove(ModifierNode m) =>
        string.Equals(m.Type, "remove", StringComparison.OrdinalIgnoreCase);

    // The keyword a category modifier names, resolved from its value (a category
    // id) via the category index, falling back to the entry's own category links.
    private static string CategoryName(ModifierNode m, Index index, SelectionEntryNode entry)
    {
        var id = m.Value.GetString() ?? "";
        if (string.IsNullOrEmpty(id))
        {
            return "";
        }
        if (index.Categories.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        return entry.CategoryLinks
            .FirstOrDefault(c => c.TargetId == id && !string.IsNullOrWhiteSpace(c.Name))?.Name ?? "";
    }

    // ownerModel is the nearest enclosing model type's name, so each option
    // records which model it is for; empty at the unit root (unit-wide options)
    // and for single-model units.
    private static void CollectOptions(
        SelectionNode node, string ownerModel, int depth, HashSet<string> visited,
        List<DatasheetOption> options, Index index, string ptsTypeId, CatalogueSchema schema, bool resolveGroupLinks)
    {
        if (depth < 0)
        {
            return;
        }
        if (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id))
        {
            return;
        }

        // A selection group's choices become options; shared across inline groups
        // and (for Crucible) group-linked ones so both surface identically.
        void AddGroup(SelectionEntryGroupNode group)
        {
            // The group's selection bounds: how many of the set may be taken.
            // A "pick one" weapon swap carries max 1.
            var (gMin, gMax) = ConstraintRange(group);
            // Honor the upstream display order of the choices (BSData sortIndex);
            // OrderBy is stable, so unindexed choices keep their document order.
            foreach (var choice in group.SelectionEntries.Cast<SelectionNode>().Concat(group.EntryLinks)
                         .OrderBy(c => c.SortIndex ?? long.MaxValue))
            {
                var (type, name, pts) = ResolveChoice(choice, index, ptsTypeId);
                if (!IsModel(type))
                {
                    var id = !string.IsNullOrEmpty(choice.Id) ? choice.Id
                        : (choice as EntryLinkNode)?.TargetId ?? "";
                    options.Add(new DatasheetOption(
                        id, name, pts, group.Name, OptionWeapons(choice, schema, index),
                        ownerModel, group.Id, gMin ?? 0, gMax ?? 0, IsGroupDefault(group, choice)));
                }
                // A model choice owns the options nested beneath it.
                var childOwner = IsModel(type) ? name : ownerModel;
                CollectOptions(choice, childOwner, depth - 1, visited, options, index, ptsTypeId, schema, resolveGroupLinks);
            }
            CollectOptions(group, ownerModel, depth - 1, visited, options, index, ptsTypeId, schema, resolveGroupLinks);
        }

        foreach (var group in node.SelectionEntryGroups)
        {
            AddGroup(group);
        }

        foreach (var child in node.SelectionEntries)
        {
            var childOwner = IsModel(child.Type) ? child.Name : ownerModel;
            CollectOptions(child, childOwner, depth - 1, visited, options, index, ptsTypeId, schema, resolveGroupLinks);
        }
        foreach (var link in node.EntryLinks)
        {
            if (!string.IsNullOrEmpty(link.TargetId) && index.Entries.TryGetValue(link.TargetId, out var target))
            {
                var childOwner = IsModel(target.Type)
                    ? (string.IsNullOrWhiteSpace(link.Name) ? target.Name : link.Name)
                    : ownerModel;
                CollectOptions(target, childOwner, depth - 1, visited, options, index, ptsTypeId, schema, resolveGroupLinks);
            }
            else if (resolveGroupLinks && !string.IsNullOrEmpty(link.TargetId)
                     && index.Groups.TryGetValue(link.TargetId, out var linkedGroup))
            {
                AddGroup(linkedGroup);
            }
        }
    }

    private static (string Type, string Name, int Points) ResolveChoice(SelectionNode choice, Index index, string ptsTypeId)
    {
        var type = choice is SelectionEntryNode se ? se.Type : "";
        var name = choice.Name;
        var pts = PointsOf(choice, ptsTypeId);

        if (choice is EntryLinkNode link && !string.IsNullOrEmpty(link.TargetId)
            && index.Entries.TryGetValue(link.TargetId, out var target))
        {
            if (string.IsNullOrWhiteSpace(name)) name = target.Name;
            if (string.IsNullOrEmpty(type)) type = target.Type;
            if (pts == 0) pts = PointsOf(target, ptsTypeId);
        }
        return (type, name, pts);
    }

    private static int PointsOf(SelectionNode node, string ptsTypeId) =>
        string.IsNullOrEmpty(ptsTypeId)
            ? 0
            : node.Costs
                .Where(c => c.TypeId == ptsTypeId)
                .Select(c => (int)Math.Round(c.Value))
                .FirstOrDefault();

    private static IReadOnlyList<Characteristic> ToCharacteristics(ProfileNode profile) =>
        profile.Characteristics
            .Select(c => new Characteristic(c.Name, c.Text ?? ""))
            .ToList();

    private static Citation? CitationFor(ProfileNode profile, Index index) =>
        ResolveCitation(profile.PublicationId, profile.Page, index);

    private static Citation? CitationFor(RuleNode rule, Index index) =>
        ResolveCitation(rule.PublicationId, rule.Page, index);

    // Resolves a profile/rule's publicationId + page into a display citation:
    // the book's short name (fallback to its full title), and the page. Null when
    // no publication is cited and no page is given, so uncited content stays clean.
    private static Citation? ResolveCitation(string publicationId, string page, Index index)
    {
        var book = "";
        if (!string.IsNullOrEmpty(publicationId) && index.Publications.TryGetValue(publicationId, out var pub))
        {
            book = !string.IsNullOrWhiteSpace(pub.ShortName) ? pub.ShortName.Trim() : pub.Name.Trim();
        }
        var pg = (page ?? "").Trim();
        return string.IsNullOrEmpty(book) && string.IsNullOrEmpty(pg) ? null : new Citation(book, pg);
    }

    // An ability's rules text: the "Description" characteristic if present, else
    // all non-empty characteristic texts joined.
    private static string DescriptionOf(ProfileNode profile)
    {
        var desc = profile.Characteristics
            .FirstOrDefault(c => string.Equals(c.Name, "Description", StringComparison.OrdinalIgnoreCase))?.Text;
        if (!string.IsNullOrWhiteSpace(desc))
        {
            return desc.Trim();
        }
        return string.Join(" ", profile.Characteristics
            .Select(c => c.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
    }

    // Detachment rules: ability-type profiles on the detachment entry and its
    // nested entries/groups, excluding the "<Detachment> Enhancements" subtree.
    private static List<Ability> CollectAbilities(SelectionNode node, IReadOnlySet<string> abilityTypes, int depth)
    {
        var result = new List<Ability>();
        Walk(node, depth);
        return result.DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        void Walk(SelectionNode n, int d)
        {
            if (d < 0)
            {
                return;
            }
            foreach (var p in n.Profiles.Where(p => abilityTypes.Contains(p.TypeName) && !string.IsNullOrWhiteSpace(p.Name)))
            {
                result.Add(new Ability(p.Name, DescriptionOf(p)));
            }
            // Detachment rules are free-text rule nodes, not ability profiles.
            foreach (var r in n.Rules.Where(r => !r.Hidden && !string.IsNullOrWhiteSpace(r.Name)))
            {
                result.Add(new Ability(r.Name, (r.Description ?? "").Trim()));
            }
            foreach (var child in n.SelectionEntries)
            {
                Walk(child, d - 1);
            }
            foreach (var group in n.SelectionEntryGroups)
            {
                if (!group.Name.EndsWith(" Enhancements", StringComparison.OrdinalIgnoreCase))
                {
                    Walk(group, d - 1);
                }
            }
        }
    }

    private static string ResolvePointsTypeId(GameSystemDocument system, CatalogueSchema schema)
    {
        if (string.IsNullOrEmpty(schema.PointsCostName))
        {
            return "";
        }
        var pts = system.CostTypes.FirstOrDefault(c => string.Equals(c.Name, schema.PointsCostName, StringComparison.OrdinalIgnoreCase))
                  ?? system.CostTypes.FirstOrDefault(c => c.Name.Contains(schema.PointsCostName, StringComparison.OrdinalIgnoreCase));
        return pts?.Id ?? "";
    }

    // Entry type "model"/"unit" marks a model-count bracket, not an add-on.
    private static bool IsModel(string type) =>
        string.Equals(type, "model", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "unit", StringComparison.OrdinalIgnoreCase);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new FormatException($"Could not deserialize {typeof(T).Name}.");

    // The catalogue an entry was defined in: its name, and whether it is a shared
    // library (allied/optional content) rather than the game system or a faction.
    private sealed record CatalogueOrigin(string Name, bool IsLibrary);

    // Id lookups across all loaded documents.
    private sealed class Index
    {
        public required Dictionary<string, ProfileNode> Profiles { get; init; }
        public required Dictionary<string, RuleNode> Rules { get; init; }
        public required Dictionary<string, InfoGroupNode> InfoGroups { get; init; }
        public required Dictionary<string, SelectionEntryNode> Entries { get; init; }
        public required Dictionary<string, SelectionEntryGroupNode> Groups { get; init; }
        // Where each entry id was defined, so library-sourced datasheets can be gated.
        public required Dictionary<string, CatalogueOrigin> EntryOrigin { get; init; }
        // Category entry id -> name, so a "category" modifier (which names its
        // target category by id) can be resolved to the keyword it grants/strips,
        // and a leader association's "instanceOf" conditions can be named.
        public required Dictionary<string, string> Categories { get; init; }
        // Source books keyed by publicationId, so a profile/rule citation resolves to a title.
        public required Dictionary<string, Publication> Publications { get; init; }

        public static Index Build(IEnumerable<DataDocument> documents)
        {
            var refs = new Refs();
            var entries = new Dictionary<string, SelectionEntryNode>();
            var groups = new Dictionary<string, SelectionEntryGroupNode>();
            var origins = new Dictionary<string, CatalogueOrigin>();
            var categories = new Dictionary<string, string>(StringComparer.Ordinal);
            var publications = new Dictionary<string, Publication>();

            foreach (var doc in documents)
            {
                // The game system and faction catalogues are core; only a linked
                // shared library (library: true) is optional/allied content.
                var origin = new CatalogueOrigin(doc.Name, (doc as CatalogueDocument)?.Library ?? false);
                foreach (var category in doc.CategoryEntries)
                {
                    if (!string.IsNullOrEmpty(category.Id)) categories.TryAdd(category.Id, category.Name);
                }
                foreach (var publication in doc.Publications)
                {
                    if (!string.IsNullOrEmpty(publication.Id)) publications.TryAdd(publication.Id, publication);
                }
                foreach (var profile in doc.SharedProfiles)
                {
                    refs.Profiles.TryAdd(profile.Id, profile);
                }
                foreach (var rule in doc.SharedRules)
                {
                    if (!string.IsNullOrEmpty(rule.Id)) refs.Rules.TryAdd(rule.Id, rule);
                }
                foreach (var ig in doc.SharedInfoGroups)
                {
                    WalkInfoGroup(ig, refs);
                }
                foreach (var entry in doc.SharedSelectionEntries)
                {
                    WalkEntry(entry, origin, refs, entries, groups, origins);
                }
                foreach (var group in doc.SharedSelectionEntryGroups)
                {
                    WalkGroup(group, origin, refs, entries, groups, origins);
                }
            }

            return new Index
            {
                Profiles = refs.Profiles, Rules = refs.Rules, InfoGroups = refs.InfoGroups,
                Entries = entries, Groups = groups, EntryOrigin = origins, Categories = categories, Publications = publications,
            };
        }

        // The id-keyed collections an infoLink can resolve against, grouped so the
        // walk methods carry one argument instead of three.
        private sealed class Refs
        {
            public Dictionary<string, ProfileNode> Profiles { get; } = new();
            public Dictionary<string, RuleNode> Rules { get; } = new();
            public Dictionary<string, InfoGroupNode> InfoGroups { get; } = new();
        }

        private static void WalkEntry(
            SelectionEntryNode entry, CatalogueOrigin origin, Refs refs,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups,
            Dictionary<string, CatalogueOrigin> origins)
        {
            if (!string.IsNullOrEmpty(entry.Id))
            {
                entries.TryAdd(entry.Id, entry);
                origins.TryAdd(entry.Id, origin);
            }
            WalkChildren(entry, origin, refs, entries, groups, origins);
        }

        private static void WalkGroup(
            SelectionEntryGroupNode group, CatalogueOrigin origin, Refs refs,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups,
            Dictionary<string, CatalogueOrigin> origins)
        {
            if (!string.IsNullOrEmpty(group.Id))
            {
                groups.TryAdd(group.Id, group);
            }
            WalkChildren(group, origin, refs, entries, groups, origins);
        }

        private static void WalkChildren(
            SelectionNode node, CatalogueOrigin origin, Refs refs,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups,
            Dictionary<string, CatalogueOrigin> origins)
        {
            foreach (var profile in node.Profiles)
            {
                if (!string.IsNullOrEmpty(profile.Id))
                {
                    refs.Profiles.TryAdd(profile.Id, profile);
                }
            }
            foreach (var rule in node.Rules)
            {
                if (!string.IsNullOrEmpty(rule.Id)) refs.Rules.TryAdd(rule.Id, rule);
            }
            foreach (var ig in node.InfoGroups)
            {
                WalkInfoGroup(ig, refs);
            }
            foreach (var child in node.SelectionEntries)
            {
                WalkEntry(child, origin, refs, entries, groups, origins);
            }
            foreach (var group in node.SelectionEntryGroups)
            {
                WalkGroup(group, origin, refs, entries, groups, origins);
            }
        }

        // Index an info group by id, plus the profiles/rules it defines and any
        // groups nested inside it, so a link to the group or to its contents
        // resolves.
        private static void WalkInfoGroup(InfoGroupNode group, Refs refs)
        {
            if (!string.IsNullOrEmpty(group.Id))
            {
                refs.InfoGroups.TryAdd(group.Id, group);
            }
            foreach (var profile in group.Profiles)
            {
                if (!string.IsNullOrEmpty(profile.Id)) refs.Profiles.TryAdd(profile.Id, profile);
            }
            foreach (var rule in group.Rules)
            {
                if (!string.IsNullOrEmpty(rule.Id)) refs.Rules.TryAdd(rule.Id, rule);
            }
            foreach (var nested in group.InfoGroups)
            {
                WalkInfoGroup(nested, refs);
            }
        }
    }
}
