using System.Text.Json;
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
        CatalogueSchema schema) =>
        factions.Select(f => ParseOne(system, f, libraries, schema)).ToList();

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
        var librariesById = BuildLibraryLookup(libraries);
        var datasheets = ExtractDatasheets(catalogue, schema, ptsTypeId, index, librariesById);
        var equipment = ExtractFactionEquipment(catalogue, schema);
        var detachments = ExtractDetachments(system, catalogue, schema, ptsTypeId, librariesById);

        var info = new GameSystemInfo(system.Id, system.Name, system.Revision);
        return new CatalogueModel(info, catalogue.Id, catalogue.Name, datasheets, equipment, detachments);
    }

    // Detachments: child entries of a selection group named per the schema (40k's
    // "Detachment"), each paired with its enhancements — the entries under a
    // "<Detachment> Enhancements" group that carry an enhancement counter cost,
    // snapshotting the enhancement's pts cost. Names only, no rules text.
    private static IReadOnlyList<Detachment> ExtractDetachments(
        GameSystemDocument system,
        CatalogueDocument catalogue,
        CatalogueSchema schema,
        string ptsTypeId,
        IReadOnlyDictionary<string, CatalogueDocument> librariesById)
    {
        if (string.IsNullOrEmpty(schema.DetachmentGroupName))
        {
            return [];
        }

        var enhCostId = system.CostTypes
            .FirstOrDefault(c => string.Equals(c.Name, schema.EnhancementCostName, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        var dpCostId = system.CostTypes
            .FirstOrDefault(c => string.Equals(c.Name, schema.DetachmentPointsCostName, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        var docs = new List<CatalogueDocument> { catalogue };
        foreach (var link in catalogue.CatalogueLinks)
        {
            if (!string.IsNullOrEmpty(link.TargetId) && librariesById.TryGetValue(link.TargetId, out var library))
            {
                docs.Add(library);
            }
        }

        var options = new List<(string Id, string Name, int Dp, List<Ability> Rules)>();
        var enhancements = new List<(string Det, string Id, string Name, int Points, string Text)>();
        foreach (var doc in docs)
        {
            foreach (var root in doc.SharedSelectionEntryGroups.Cast<SelectionNode>().Concat(doc.SharedSelectionEntries))
            {
                CollectDetachments(root, null, DetachmentMaxDepth, [], schema.DetachmentGroupName, enhCostId, dpCostId, ptsTypeId, schema.AbilityTypes, options, enhancements);
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
        // detachment rules.
        if (node is SelectionEntryGroupNode && string.Equals(node.Name, detachmentGroupName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var child in node.SelectionEntries)
            {
                if (!string.IsNullOrEmpty(child.Id))
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
            CollectDetachments(child, groupName, depth - 1, visited, detachmentGroupName, enhCostId, dpCostId, ptsTypeId, abilityTypes, options, enhancements);
        }
        foreach (var group in node.SelectionEntryGroups)
        {
            CollectDetachments(group, group.Name, depth - 1, visited, detachmentGroupName, enhCostId, dpCostId, ptsTypeId, abilityTypes, options, enhancements);
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
    // resolve to, and the entries of any library catalogue it imports.
    private static List<Datasheet> ExtractDatasheets(
        CatalogueDocument faction,
        CatalogueSchema schema,
        string ptsTypeId,
        Index index,
        IReadOnlyDictionary<string, CatalogueDocument> librariesById)
    {
        var candidates = new List<SelectionEntryNode>();
        CollectCandidates(faction, candidates, index);

        foreach (var link in faction.CatalogueLinks)
        {
            if (!string.IsNullOrEmpty(link.TargetId) && librariesById.TryGetValue(link.TargetId, out var library))
            {
                CollectCandidates(library, candidates, index);
            }
        }

        return candidates
            .Select(entry => ToDatasheet(entry, schema, ptsTypeId, index))
            .OfType<Datasheet>()
            .DistinctBy(d => d.Id)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static Dictionary<string, CatalogueDocument> BuildLibraryLookup(IEnumerable<CatalogueDocument> libraries)
    {
        var map = new Dictionary<string, CatalogueDocument>();
        foreach (var library in libraries)
        {
            if (!string.IsNullOrEmpty(library.Id))
            {
                map.TryAdd(library.Id, library);
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

        var keywords = entry.CategoryLinks
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        var innateProfiles = new List<ProfileNode>();
        Gather(entry, MaxDepth, [], innateProfiles, index, descendGroups: false);
        var abilities = innateProfiles
            .Where(p => schema.AbilityTypes.Contains(p.TypeName) && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Ability(p.Name, DescriptionOf(p)))
            .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = ExtractOptions(entry, schema, ptsTypeId, index);

        var unit = unitProfiles
            .Select(p => new UnitProfile(p.Name, ToCharacteristics(p)))
            .ToList();

        return new Datasheet(entry.Id, entry.Name, points, keywords, unit, weapons, abilities, options, equipment);
    }

    // Collects profiles from a node, its children, and any entries/profiles its
    // links resolve to, across the game system, catalogue, and libraries.
    private static void Gather(SelectionNode node, int depth, HashSet<string> visited, List<ProfileNode> profiles, Index index, bool descendGroups)
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

        if (node is EntryLinkNode link && !string.IsNullOrEmpty(link.TargetId))
        {
            if (index.Profiles.TryGetValue(link.TargetId, out var profile))
            {
                profiles.Add(profile);
            }
            else if (index.Entries.TryGetValue(link.TargetId, out var entry))
            {
                Gather(entry, depth - 1, visited, profiles, index, descendGroups);
            }
            else if (index.Groups.TryGetValue(link.TargetId, out var group))
            {
                Gather(group, depth - 1, visited, profiles, index, descendGroups);
            }
        }

        foreach (var child in node.SelectionEntries)
        {
            Gather(child, depth - 1, visited, profiles, index, descendGroups);
        }

        if (descendGroups)
        {
            foreach (var group in node.SelectionEntryGroups)
            {
                Gather(group, depth - 1, visited, profiles, index, descendGroups);
            }
        }

        foreach (var childLink in node.EntryLinks)
        {
            Gather(childLink, depth - 1, visited, profiles, index, descendGroups);
        }
    }

    // Wargear/upgrade choices the datasheet offers: the children of the unit's
    // selection groups, with their additive pts cost (0 when free or point-less).
    // Model/unit children are size brackets, not add-ons, so they are skipped.
    private static IReadOnlyList<DatasheetOption> ExtractOptions(SelectionEntryNode entry, CatalogueSchema schema, string ptsTypeId, Index index)
    {
        var options = new List<DatasheetOption>();
        CollectOptions(entry, MaxDepth, [], options, index, ptsTypeId);
        return options
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && !string.IsNullOrEmpty(o.Id))
            .DistinctBy(o => o.Id)
            .ToList();
    }

    private static void CollectOptions(
        SelectionNode node, int depth, HashSet<string> visited,
        List<DatasheetOption> options, Index index, string ptsTypeId)
    {
        if (depth < 0)
        {
            return;
        }
        if (!string.IsNullOrEmpty(node.Id) && !visited.Add(node.Id))
        {
            return;
        }

        foreach (var group in node.SelectionEntryGroups)
        {
            foreach (var choice in group.SelectionEntries.Cast<SelectionNode>().Concat(group.EntryLinks))
            {
                var (type, name, pts) = ResolveChoice(choice, index, ptsTypeId);
                if (!IsModel(type))
                {
                    var id = !string.IsNullOrEmpty(choice.Id) ? choice.Id
                        : (choice as EntryLinkNode)?.TargetId ?? "";
                    options.Add(new DatasheetOption(id, name, pts, group.Name));
                }
                CollectOptions(choice, depth - 1, visited, options, index, ptsTypeId);
            }
            CollectOptions(group, depth - 1, visited, options, index, ptsTypeId);
        }

        foreach (var child in node.SelectionEntries)
        {
            CollectOptions(child, depth - 1, visited, options, index, ptsTypeId);
        }
        foreach (var link in node.EntryLinks)
        {
            if (!string.IsNullOrEmpty(link.TargetId) && index.Entries.TryGetValue(link.TargetId, out var target))
            {
                CollectOptions(target, depth - 1, visited, options, index, ptsTypeId);
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

    // Id lookups across all loaded documents.
    private sealed class Index
    {
        public required Dictionary<string, ProfileNode> Profiles { get; init; }
        public required Dictionary<string, SelectionEntryNode> Entries { get; init; }
        public required Dictionary<string, SelectionEntryGroupNode> Groups { get; init; }

        public static Index Build(IEnumerable<DataDocument> documents)
        {
            var profiles = new Dictionary<string, ProfileNode>();
            var entries = new Dictionary<string, SelectionEntryNode>();
            var groups = new Dictionary<string, SelectionEntryGroupNode>();

            foreach (var doc in documents)
            {
                foreach (var profile in doc.SharedProfiles)
                {
                    profiles.TryAdd(profile.Id, profile);
                }
                foreach (var entry in doc.SharedSelectionEntries)
                {
                    WalkEntry(entry, profiles, entries, groups);
                }
                foreach (var group in doc.SharedSelectionEntryGroups)
                {
                    WalkGroup(group, profiles, entries, groups);
                }
            }

            return new Index { Profiles = profiles, Entries = entries, Groups = groups };
        }

        private static void WalkEntry(
            SelectionEntryNode entry,
            Dictionary<string, ProfileNode> profiles,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups)
        {
            if (!string.IsNullOrEmpty(entry.Id))
            {
                entries.TryAdd(entry.Id, entry);
            }
            WalkChildren(entry, profiles, entries, groups);
        }

        private static void WalkGroup(
            SelectionEntryGroupNode group,
            Dictionary<string, ProfileNode> profiles,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups)
        {
            if (!string.IsNullOrEmpty(group.Id))
            {
                groups.TryAdd(group.Id, group);
            }
            WalkChildren(group, profiles, entries, groups);
        }

        private static void WalkChildren(
            SelectionNode node,
            Dictionary<string, ProfileNode> profiles,
            Dictionary<string, SelectionEntryNode> entries,
            Dictionary<string, SelectionEntryGroupNode> groups)
        {
            foreach (var profile in node.Profiles)
            {
                if (!string.IsNullOrEmpty(profile.Id))
                {
                    profiles.TryAdd(profile.Id, profile);
                }
            }
            foreach (var child in node.SelectionEntries)
            {
                WalkEntry(child, profiles, entries, groups);
            }
            foreach (var group in node.SelectionEntryGroups)
            {
                WalkGroup(group, profiles, entries, groups);
            }
        }
    }
}
