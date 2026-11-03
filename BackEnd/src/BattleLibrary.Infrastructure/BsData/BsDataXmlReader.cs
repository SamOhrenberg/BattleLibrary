using System.Xml.Linq;
using BattleLibrary.Infrastructure.BsData.Json;

namespace BattleLibrary.Infrastructure.BsData;

// Reads classic BattleScribe XML (.gst/.cat, used by Kill Team) into the same
// document DTOs the JSON parser consumes. The schema is identical; only the
// serialization differs. Namespaces are matched by local name. A catalogue's
// non-shared top-level entries/groups are merged into the shared lists so the
// parser promotes them the same way it does 11e's shared entries.
internal static class BsDataXmlReader
{
    public static GameSystemDocument ReadGameSystem(string xml)
    {
        var root = Root(xml, "gameSystem");
        var doc = new GameSystemDocument();
        FillDocument(doc, root);
        return doc;
    }

    public static CatalogueDocument ReadCatalogue(string xml)
    {
        var root = Root(xml, "catalogue");
        var doc = new CatalogueDocument
        {
            GameSystemId = Attr(root, "gameSystemId"),
            Library = Bool(root, "library"),
        };
        FillDocument(doc, root);
        foreach (var link in Children(root, "catalogueLinks", "catalogueLink"))
        {
            doc.CatalogueLinks.Add(new CatalogueLink
            {
                Id = Attr(link, "id"),
                Name = Attr(link, "name"),
                TargetId = Attr(link, "targetId"),
            });
        }
        return doc;
    }

    private static void FillDocument(DataDocument doc, XElement root)
    {
        doc.Id = Attr(root, "id");
        doc.Name = Attr(root, "name");
        doc.Revision = Int(root, "revision");

        foreach (var c in Children(root, "costTypes", "costType"))
        {
            doc.CostTypes.Add(new CostType { Id = Attr(c, "id"), Name = Attr(c, "name") });
        }
        foreach (var p in Children(root, "profileTypes", "profileType"))
        {
            doc.ProfileTypes.Add(new ProfileType { Id = Attr(p, "id"), Name = Attr(p, "name") });
        }
        foreach (var c in Children(root, "categoryEntries", "categoryEntry"))
        {
            doc.CategoryEntries.Add(new CategoryEntry { Id = Attr(c, "id"), Name = Attr(c, "name") });
        }
        foreach (var f in Children(root, "forceEntries", "forceEntry"))
        {
            doc.ForceEntries.Add(new ForceEntry { Id = Attr(f, "id"), Name = Attr(f, "name") });
        }
        foreach (var p in Children(root, "sharedProfiles", "profile"))
        {
            doc.SharedProfiles.Add(ReadProfile(p));
        }
        foreach (var r in Children(root, "sharedRules", "rule"))
        {
            doc.SharedRules.Add(ReadRule(r));
        }
        foreach (var g in Children(root, "sharedInfoGroups", "infoGroup"))
        {
            doc.SharedInfoGroups.Add(ReadInfoGroup(g));
        }

        // Shared and non-shared top-level entries/groups both seed candidates.
        foreach (var e in Children(root, "sharedSelectionEntries", "selectionEntry")
                     .Concat(Children(root, "selectionEntries", "selectionEntry")))
        {
            doc.SharedSelectionEntries.Add(ReadEntry(e));
        }
        foreach (var g in Children(root, "sharedSelectionEntryGroups", "selectionEntryGroup")
                     .Concat(Children(root, "selectionEntryGroups", "selectionEntryGroup")))
        {
            doc.SharedSelectionEntryGroups.Add(ReadGroup(g));
        }
        foreach (var l in Children(root, "entryLinks", "entryLink"))
        {
            doc.EntryLinks.Add(ReadLink(l));
        }
    }

    private static SelectionEntryNode ReadEntry(XElement e)
    {
        var node = new SelectionEntryNode { Type = Attr(e, "type") };
        FillSelection(node, e);
        return node;
    }

    private static SelectionEntryGroupNode ReadGroup(XElement e)
    {
        var node = new SelectionEntryGroupNode { DefaultSelectionEntryId = Attr(e, "defaultSelectionEntryId") };
        FillSelection(node, e);
        return node;
    }

    private static EntryLinkNode ReadLink(XElement e)
    {
        var node = new EntryLinkNode { Type = Attr(e, "type"), TargetId = Attr(e, "targetId") };
        FillSelection(node, e);
        return node;
    }

    private static void FillSelection(SelectionNode node, XElement e)
    {
        node.Id = Attr(e, "id");
        node.Name = Attr(e, "name");
        node.Hidden = Bool(e, "hidden");

        foreach (var c in Children(e, "costs", "cost"))
        {
            node.Costs.Add(new Cost { Name = Attr(c, "name"), TypeId = Attr(c, "typeId"), Value = Double(c, "value") });
        }
        foreach (var p in Children(e, "profiles", "profile"))
        {
            node.Profiles.Add(ReadProfile(p));
        }
        foreach (var c in Children(e, "categoryLinks", "categoryLink"))
        {
            node.CategoryLinks.Add(new CategoryLink
            {
                Id = Attr(c, "id"),
                Name = Attr(c, "name"),
                TargetId = Attr(c, "targetId"),
                Primary = Bool(c, "primary"),
            });
        }
        foreach (var child in Children(e, "selectionEntries", "selectionEntry"))
        {
            node.SelectionEntries.Add(ReadEntry(child));
        }
        foreach (var g in Children(e, "selectionEntryGroups", "selectionEntryGroup"))
        {
            node.SelectionEntryGroups.Add(ReadGroup(g));
        }
        foreach (var l in Children(e, "entryLinks", "entryLink"))
        {
            node.EntryLinks.Add(ReadLink(l));
        }
        foreach (var r in Children(e, "rules", "rule"))
        {
            node.Rules.Add(ReadRule(r));
        }
        foreach (var l in Children(e, "infoLinks", "infoLink"))
        {
            node.InfoLinks.Add(ReadInfoLink(l));
        }
        foreach (var g in Children(e, "infoGroups", "infoGroup"))
        {
            node.InfoGroups.Add(ReadInfoGroup(g));
        }
    }

    private static RuleNode ReadRule(XElement e)
    {
        var desc = e.Elements().FirstOrDefault(x => x.Name.LocalName == "description");
        return new RuleNode
        {
            Id = Attr(e, "id"),
            Name = Attr(e, "name"),
            Hidden = Bool(e, "hidden"),
            Description = desc?.Value,
        };
    }

    private static InfoLinkNode ReadInfoLink(XElement e) => new()
    {
        Id = Attr(e, "id"),
        Name = Attr(e, "name"),
        Type = Attr(e, "type"),
        TargetId = Attr(e, "targetId"),
        Hidden = Bool(e, "hidden"),
    };

    private static InfoGroupNode ReadInfoGroup(XElement e)
    {
        var node = new InfoGroupNode
        {
            Id = Attr(e, "id"),
            Name = Attr(e, "name"),
            Hidden = Bool(e, "hidden"),
        };
        foreach (var p in Children(e, "profiles", "profile"))
        {
            node.Profiles.Add(ReadProfile(p));
        }
        foreach (var r in Children(e, "rules", "rule"))
        {
            node.Rules.Add(ReadRule(r));
        }
        foreach (var l in Children(e, "infoLinks", "infoLink"))
        {
            node.InfoLinks.Add(ReadInfoLink(l));
        }
        foreach (var g in Children(e, "infoGroups", "infoGroup"))
        {
            node.InfoGroups.Add(ReadInfoGroup(g));
        }
        return node;
    }

    private static ProfileNode ReadProfile(XElement e)
    {
        var profile = new ProfileNode
        {
            Id = Attr(e, "id"),
            Name = Attr(e, "name"),
            TypeId = Attr(e, "typeId"),
            TypeName = Attr(e, "typeName"),
            Hidden = Bool(e, "hidden"),
        };
        var chars = e.Elements().FirstOrDefault(x => x.Name.LocalName == "characteristics");
        if (chars is not null)
        {
            foreach (var c in chars.Elements().Where(x => x.Name.LocalName == "characteristic"))
            {
                profile.Characteristics.Add(new CharacteristicNode
                {
                    Name = Attr(c, "name"),
                    TypeId = Attr(c, "typeId"),
                    Text = c.Value,
                });
            }
        }
        return profile;
    }

    // ---- XML helpers (namespace-agnostic) ----

    private static XElement Root(string xml, string localName)
    {
        var root = XDocument.Parse(xml).Root
                   ?? throw new FormatException("Empty XML document.");
        if (root.Name.LocalName != localName)
        {
            throw new FormatException($"Expected <{localName}> root, found <{root.Name.LocalName}>.");
        }
        return root;
    }

    private static IEnumerable<XElement> Children(XElement parent, string wrapper, string item)
    {
        var w = parent.Elements().FirstOrDefault(e => e.Name.LocalName == wrapper);
        return w is null ? [] : w.Elements().Where(e => e.Name.LocalName == item);
    }

    private static string Attr(XElement e, string name) => e.Attribute(name)?.Value ?? "";

    private static bool Bool(XElement e, string name) =>
        string.Equals(Attr(e, name), "true", StringComparison.OrdinalIgnoreCase);

    private static int Int(XElement e, string name) =>
        int.TryParse(Attr(e, name), out var v) ? v : 0;

    private static double Double(XElement e, string name) =>
        double.TryParse(Attr(e, name), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
