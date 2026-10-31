using BattleLibrary.Infrastructure.Rosters;
using Xunit;

namespace BattleLibrary.Tests;

// Round-trips a roster through the writer and reader; no BSData needed.
public class RosterXmlTests
{
    private static RosterDocument Sample() => new(
        "Test List", "sys-123", "Warhammer 40,000", 7, "pts-type",
        [
            new RosterForce("fac-1", "Orks", 42,
            [
                new RosterSelection("unit-boyz", "Boyz", 1, "unit", 90,
                [
                    new RosterSelection("opt-nob", "Boss Nob", 1, "upgrade", 0, []),
                ]),
                new RosterSelection("det-waaagh", "War Horde", 1, "upgrade", 0, []),
            ]),
        ]);

    [Fact]
    public void Write_then_read_preserves_roster()
    {
        var xml = RosterXml.Write(Sample());
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var back = RosterXml.Read(stream);

        Assert.Equal("Test List", back.Name);
        Assert.Equal("sys-123", back.GameSystemId);
        Assert.Equal("pts-type", back.PointsTypeId);
        var force = Assert.Single(back.Forces);
        Assert.Equal("fac-1", force.CatalogueId);
        Assert.Equal(42, force.CatalogueRevision);
        Assert.Equal(2, force.Selections.Count);

        var boyz = force.Selections.First(s => s.EntryId == "unit-boyz");
        Assert.Equal(90, boyz.Points);
        Assert.Equal("unit", boyz.Type);
        Assert.Equal("opt-nob", Assert.Single(boyz.Selections).EntryId);
    }

    [Fact]
    public void Zip_then_read_preserves_roster()
    {
        var xml = RosterXml.Write(Sample());
        var zipped = RosterXml.Zip("test-list", xml);
        using var stream = new MemoryStream(zipped);
        var back = RosterXml.Read(stream);

        Assert.Equal("Test List", back.Name);
        Assert.Equal(90, back.Forces[0].Selections.First(s => s.EntryId == "unit-boyz").Points);
    }

    [Fact]
    public void Write_then_read_preserves_custom_name()
    {
        var roster = new RosterDocument(
            "Named List", "sys-123", "Warhammer 40,000", 7, "pts-type",
            [
                new RosterForce("fac-1", "Orks", 42,
                [
                    new RosterSelection("unit-boss", "Warboss", 1, "unit", 80, [], "Ghazghkull"),
                ]),
            ]);

        var xml = RosterXml.Write(roster);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var back = RosterXml.Read(stream);

        var boss = Assert.Single(back.Forces[0].Selections);
        Assert.Equal("Ghazghkull", boss.CustomName);
    }

    [Fact]
    public void Write_omits_custom_name_when_absent()
    {
        var xml = RosterXml.Write(Sample());
        Assert.DoesNotContain("customName", xml);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var back = RosterXml.Read(stream);
        Assert.Null(back.Forces[0].Selections.First(s => s.EntryId == "unit-boyz").CustomName);
    }

    [Fact]
    public void Write_then_read_preserves_multiple_forces()
    {
        var roster = new RosterDocument(
            "Allied List", "sys-123", "Warhammer 40,000", 7, "pts-type",
            [
                new RosterForce("fac-1", "Orks", 42,
                [
                    new RosterSelection("unit-boyz", "Boyz", 1, "unit", 90, []),
                ]),
                new RosterForce("fac-2", "Imperium - Agents", 9,
                [
                    new RosterSelection("unit-assassin", "Callidus", 1, "unit", 100, []),
                ]),
            ]);

        var xml = RosterXml.Write(roster);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var back = RosterXml.Read(stream);

        Assert.Equal(2, back.Forces.Count);
        Assert.Equal("fac-1", back.Forces[0].CatalogueId);
        Assert.Equal("fac-2", back.Forces[1].CatalogueId);
        Assert.Equal("unit-assassin", Assert.Single(back.Forces[1].Selections).EntryId);
    }

    [Fact]
    public void Read_rejects_non_roster_xml()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<notARoster/>"));
        Assert.Throws<FormatException>(() => RosterXml.Read(stream));
    }
}
