using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace BattleLibrary.Infrastructure.Rosters;

// Reads and writes BattleScribe roster files. A .ros is the roster XML; a .rosz
// is that XML zipped as a single entry. The writer emits the rosterSchema
// namespace BattleScribe/NewRecruit expect; the reader matches by local name so
// it accepts files with or without the namespace.
public static class RosterXml
{
    private const string Ns = "http://www.battlescribe.net/schema/rosterSchema";
    private const string BattleScribeVersion = "2.03";
    private static readonly XNamespace N = Ns;

    public static string Write(RosterDocument roster)
    {
        var total = roster.Forces.SelectMany(f => f.Selections).Sum(TotalPoints);

        var rosterEl = new XElement(N + "roster",
            new XAttribute("id", NewId()),
            new XAttribute("name", roster.Name),
            new XAttribute("battleScribeVersion", BattleScribeVersion),
            new XAttribute("gameSystemId", roster.GameSystemId),
            new XAttribute("gameSystemName", roster.GameSystemName),
            new XAttribute("gameSystemRevision", roster.GameSystemRevision),
            Costs(roster.PointsTypeId, total),
            new XElement(N + "forces",
                roster.Forces.Select(f => Force(f, roster.GameSystemId, roster.PointsTypeId))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), rosterEl);
        return doc.Declaration + Environment.NewLine + doc;
    }

    // Zips the roster XML into a .rosz (one entry, "<name>.ros").
    public static byte[] Zip(string fileBaseName, string rosXml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"{fileBaseName}.ros", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(rosXml);
        }
        return buffer.ToArray();
    }

    // Parses a .ros (raw XML) or .rosz (zip) stream into a RosterDocument.
    public static RosterDocument Read(Stream input)
    {
        var bytes = ReadAll(input);
        var xml = IsZip(bytes) ? ExtractRos(bytes) : System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');

        var root = XDocument.Parse(xml).Root
                   ?? throw new FormatException("Empty roster file.");
        if (!string.Equals(root.Name.LocalName, "roster", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Not a BattleScribe roster (missing <roster> root).");
        }

        var ptsTypeId = PointsTypeIdOf(root);
        var forces = Child(root, "forces")?.Elements().Where(IsForce)
            .Select(f => ReadForce(f, ptsTypeId)).ToList() ?? [];

        return new RosterDocument(
            Attr(root, "name"),
            Attr(root, "gameSystemId"),
            Attr(root, "gameSystemName"),
            IntAttr(root, "gameSystemRevision"),
            ptsTypeId,
            forces);
    }

    private static XElement Force(RosterForce force, string gameSystemId, string ptsTypeId) =>
        new(N + "force",
            new XAttribute("id", NewId()),
            new XAttribute("name", string.IsNullOrEmpty(force.ForceName) ? force.CatalogueName : force.ForceName),
            new XAttribute("entryId", force.ForceEntryId),
            new XAttribute("catalogueId", force.CatalogueId),
            new XAttribute("catalogueRevision", force.CatalogueRevision),
            new XAttribute("catalogueName", force.CatalogueName),
            new XAttribute("gameSystemId", gameSystemId),
            new XElement(N + "selections", force.Selections.Select(s => Selection(s, ptsTypeId))));

    private static XElement Selection(RosterSelection sel, string ptsTypeId)
    {
        var el = new XElement(N + "selection",
            new XAttribute("id", NewId()),
            new XAttribute("name", sel.Name),
            new XAttribute("entryId", sel.EntryId),
            new XAttribute("number", sel.Number),
            new XAttribute("type", sel.Type),
            Costs(ptsTypeId, sel.Points));
        if (!string.IsNullOrWhiteSpace(sel.CustomName))
        {
            el.Add(new XAttribute("customName", sel.CustomName));
        }
        if (sel.Selections.Count > 0)
        {
            el.Add(new XElement(N + "selections", sel.Selections.Select(c => Selection(c, ptsTypeId))));
        }
        return el;
    }

    // A costs block holding the pts cost; omitted when point-less or zero.
    private static XElement? Costs(string ptsTypeId, int points)
    {
        if (string.IsNullOrEmpty(ptsTypeId) || points == 0)
        {
            return null;
        }
        return new XElement(N + "costs",
            new XElement(N + "cost",
                new XAttribute("name", "pts"),
                new XAttribute("typeId", ptsTypeId),
                new XAttribute("value", points.ToString("0.0", CultureInfo.InvariantCulture))));
    }

    private static RosterForce ReadForce(XElement force, string ptsTypeId)
    {
        var selections = Child(force, "selections")?.Elements().Where(IsSelection)
            .Select(s => ReadSelection(s, ptsTypeId)).ToList() ?? [];
        return new RosterForce(
            Attr(force, "catalogueId"),
            Attr(force, "catalogueName"),
            IntAttr(force, "catalogueRevision"),
            selections,
            Attr(force, "entryId"),
            Attr(force, "name"));
    }

    private static RosterSelection ReadSelection(XElement sel, string ptsTypeId)
    {
        var children = Child(sel, "selections")?.Elements().Where(IsSelection)
            .Select(s => ReadSelection(s, ptsTypeId)).ToList() ?? [];
        var customName = Attr(sel, "customName");
        return new RosterSelection(
            Attr(sel, "entryId"),
            Attr(sel, "name"),
            IntAttr(sel, "number", 1),
            Attr(sel, "type"),
            PointsOf(sel, ptsTypeId),
            children,
            string.IsNullOrWhiteSpace(customName) ? null : customName);
    }

    private static int TotalPoints(RosterSelection s) =>
        s.Points + s.Selections.Sum(TotalPoints);

    // The pts cost on a selection: the "pts"-named cost, or any cost matching the
    // roster's points type id. Rounded to an int.
    private static int PointsOf(XElement selection, string ptsTypeId)
    {
        var costs = Child(selection, "costs");
        if (costs is null)
        {
            return 0;
        }
        foreach (var cost in costs.Elements().Where(e => string.Equals(e.Name.LocalName, "cost", StringComparison.OrdinalIgnoreCase)))
        {
            var isPts = string.Equals(Attr(cost, "name"), "pts", StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(ptsTypeId) && Attr(cost, "typeId") == ptsTypeId);
            if (isPts && double.TryParse(Attr(cost, "value"), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                return (int)Math.Round(v);
            }
        }
        return 0;
    }

    // The points cost-type id declared on the roster's cost types, falling back to
    // the id of the top-level "pts" cost.
    private static string PointsTypeIdOf(XElement root)
    {
        var costTypes = Child(root, "costTypes");
        if (costTypes is not null)
        {
            var pts = costTypes.Elements()
                .FirstOrDefault(e => string.Equals(Attr(e, "name"), "pts", StringComparison.OrdinalIgnoreCase));
            if (pts is not null)
            {
                return Attr(pts, "id");
            }
        }
        var cost = Child(root, "costs")?.Elements()
            .FirstOrDefault(e => string.Equals(Attr(e, "name"), "pts", StringComparison.OrdinalIgnoreCase));
        return cost is null ? "" : Attr(cost, "typeId");
    }

    private static bool IsForce(XElement e) => string.Equals(e.Name.LocalName, "force", StringComparison.OrdinalIgnoreCase);
    private static bool IsSelection(XElement e) => string.Equals(e.Name.LocalName, "selection", StringComparison.OrdinalIgnoreCase);

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string Attr(XElement e, string name) => e.Attribute(name)?.Value ?? "";

    private static int IntAttr(XElement e, string name, int fallback = 0) =>
        int.TryParse(Attr(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static string NewId() => Guid.NewGuid().ToString();

    private static byte[] ReadAll(Stream input)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool IsZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 0x03 && bytes[3] == 0x04;

    private static string ExtractRos(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".ros", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault()
                    ?? throw new FormatException("Empty .rosz archive.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd().TrimStart('﻿');
    }
}
