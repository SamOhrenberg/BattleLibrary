using System.Text;
using BattleLibrary.Domain.Catalogue;
using BattleLibrary.Infrastructure.BsData;
using Xunit;

namespace BattleLibrary.Tests;

// Data-driven: point BSDATA_TEST_DIR at a folder holding gst.json (the game
// system) and orks.json (the Orks catalogue). GW data is never committed.
public class BsDataParserTests
{
    private static (string Gst, string Orks) LoadOrSkip()
    {
        var dir = Environment.GetEnvironmentVariable("BSDATA_TEST_DIR");
        var gst = dir is null ? null : Path.Combine(dir, "gst.json");
        var orks = dir is null ? null : Path.Combine(dir, "orks.json");
        if (gst is null || orks is null || !File.Exists(gst) || !File.Exists(orks))
        {
            Assert.Skip("Set BSDATA_TEST_DIR to a folder containing gst.json and orks.json.");
        }
        return (File.ReadAllText(gst!), File.ReadAllText(orks!));
    }

    [Fact]
    public void Parses_orks_battlewagon()
    {
        var (gst, orks) = LoadOrSkip();
        var model = new BsDataParser().ParseCatalogue(gst, orks);

        Assert.Equal("Warhammer 40,000 11th Edition", model.GameSystem.Name);
        Assert.NotEmpty(model.Datasheets);

        var battlewagon = model.Datasheets.FirstOrDefault(d => d.Name == "Battlewagon");
        Assert.NotNull(battlewagon);
        Assert.Equal(160, battlewagon!.Points);
        Assert.Contains("Vehicle", battlewagon.Keywords);

        var statLine = Assert.Single(battlewagon.Profiles, p => p.Name == "Battlewagon");
        Assert.Equal("11", statLine.Characteristics.Single(c => c.Name == "T").Value);
        Assert.Equal("16", statLine.Characteristics.Single(c => c.Name == "W").Value);

        Assert.NotEmpty(battlewagon.Weapons);
    }

    [Fact]
    public void Writes_summary()
    {
        var (gst, orks) = LoadOrSkip();
        var model = new BsDataParser().ParseCatalogue(gst, orks);

        var sb = new StringBuilder();
        sb.AppendLine($"System : {model.GameSystem.Name} (rev {model.GameSystem.Revision})");
        sb.AppendLine($"Faction: {model.FactionName}");
        sb.AppendLine($"Datasheets: {model.Datasheets.Count}");
        sb.AppendLine();
        sb.AppendLine("First 15 datasheets (name | pts | #profiles | #weapons):");
        foreach (var d in model.Datasheets.Take(15))
        {
            sb.AppendLine($"  {d.Name} | {d.Points?.ToString() ?? "-"} | {d.Profiles.Count} | {d.Weapons.Count}");
        }

        var bw = model.Datasheets.First(d => d.Name == "Battlewagon");
        sb.AppendLine();
        sb.AppendLine("Battlewagon detail:");
        sb.AppendLine($"  points   : {bw.Points}");
        sb.AppendLine($"  keywords : {string.Join(", ", bw.Keywords)}");
        var line = bw.Profiles.First();
        sb.AppendLine($"  statline : {string.Join("  ", line.Characteristics.Select(c => $"{c.Name} {c.Value}"))}");
        sb.AppendLine($"  weapons  : {string.Join(", ", bw.Weapons.Select(w => w.Name))}");
        sb.AppendLine($"  abilities: {string.Join(", ", bw.Abilities)}");
        sb.AppendLine($"  options  : {string.Join(", ", bw.Options.Select(o => o.Points > 0 ? $"{o.Name} (+{o.Points})" : o.Name))}");

        var outPath = Path.Combine(Environment.GetEnvironmentVariable("BSDATA_TEST_DIR")!, "parse-summary.txt");
        File.WriteAllText(outPath, sb.ToString());

        Assert.True(model.Datasheets.Count > 10);
    }
}
