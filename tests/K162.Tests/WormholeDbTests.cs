using K162.Core;
using Xunit;

namespace K162.Tests;

public class WormholeDbTests
{
    private const string SampleJson = """
        {
          "31000005": {"n":"Thera","c":"thera","e":null,"s":[{"k":"E587","d":"c2","m":750000000}]},
          "31002318": {"n":"J111613","c":"c5","e":"Cataclysmic Variable","s":[{"k":"E175","d":"c4","m":2000000000}]}
        }
        """;

    [Fact]
    public void LoadFromJson_ParsesSystems()
    {
        var db = WormholeDb.LoadFromJson(SampleJson);
        Assert.Equal(2, db.Count);
        var thera = db.Find(31000005);
        Assert.NotNull(thera);
        Assert.Equal("Thera", thera.Name);
        Assert.Equal("", thera.Effect);
        var s = Assert.Single(thera.Statics);
        Assert.Equal("E587", s.Code);
        Assert.Equal("C2", s.DestinationClass);
    }

    [Fact]
    public void FindByName_And_TryGetId_Work()
    {
        var db = WormholeDb.LoadFromJson(SampleJson);
        Assert.NotNull(db.FindByName("J111613"));
        Assert.True(db.TryGetId("Thera", out var id));
        Assert.Equal(31000005, id);
        Assert.False(db.TryGetId("Jita", out _));
    }

    [Theory]
    [InlineData("c5", "C5")]
    [InlineData("c13", "C13 SHATTERED")]
    [InlineData("thera", "THERA")]
    [InlineData("hs", "HS")]
    [InlineData("ls", "LS")]
    [InlineData("ns", "NS")]
    [InlineData("sentinel", "DRIFTER SENTINEL")]
    public void FormatClass_MapsDisplayLabels(string raw, string expected) =>
        Assert.Equal(expected, WormholeDb.FormatClass(raw));

    [Theory]
    [InlineData(2_000_000_000, "2.0G")]
    [InlineData(3_300_000_000, "3.3G")]
    [InlineData(750_000_000, "750M")]
    [InlineData(0, "VAR")]
    public void MassLabel_Formats(long massKg, string expected) =>
        Assert.Equal(expected, new StaticConnection("X877", "C4", massKg).MassLabel);
}
