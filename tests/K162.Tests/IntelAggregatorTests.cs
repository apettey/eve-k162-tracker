using K162.Core;
using K162.Core.Intel;
using Xunit;

namespace K162.Tests;

public class IntelAggregatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static KillmailDetail Kill(DateTimeOffset time, long[]? attackerCorps = null,
        long[]? attackerChars = null, int attackers = 1) =>
        new(Random.Shared.NextInt64(), time, 31000001, 587, 98000010,
            attackerCorps ?? [98000001], attackerChars ?? [1001], attackers);

    [Fact]
    public void BuildKillBins_PlacesKillsInCorrectBins()
    {
        var kills = new[]
        {
            Kill(Now.AddMinutes(-30)),          // newest bin (23)
            Kill(Now.AddHours(-3)),             // bin 22
            Kill(Now.AddHours(-47.5)),          // oldest bin (0)
            Kill(Now.AddHours(-50)),            // outside window — dropped
        };
        var bins = IntelAggregator.BuildKillBins(kills, Now);
        Assert.Equal(24, bins.Length);
        Assert.Equal(1, bins[23]);
        Assert.Equal(1, bins[22]);
        Assert.Equal(1, bins[0]);
        Assert.Equal(3, bins.Sum());
    }

    [Theory]
    [InlineData(0, ThreatLevel.Calm)]
    [InlineData(1, ThreatLevel.Calm)]
    [InlineData(2, ThreatLevel.Warm)]
    [InlineData(5, ThreatLevel.Warm)]
    [InlineData(6, ThreatLevel.Hot)]
    [InlineData(11, ThreatLevel.Hot)]
    public void ComputeThreat_UsesLast6Hours(int recentKills, ThreatLevel expected)
    {
        var bins = new int[24];
        bins[23] = recentKills;
        bins[0] = 50; // old activity must not affect threat
        Assert.Equal(expected, IntelAggregator.ComputeThreat(bins));
    }

    [Fact]
    public void ComputeThreat_IgnoresKillsOlderThan6Hours()
    {
        var bins = new int[24];
        bins[20] = 10; // 6-8h ago — outside the 3-bin window
        Assert.Equal(ThreatLevel.Calm, IntelAggregator.ComputeThreat(bins));
    }

    [Fact]
    public void BuildResidents_RanksByKillParticipation_AndCountsDistinctPilots()
    {
        var kills = new[]
        {
            Kill(Now.AddHours(-1), [98000001], [1, 2]),
            Kill(Now.AddHours(-2), [98000001], [1, 3]),
            Kill(Now.AddHours(-3), [98000002], [9]),
        };
        var residents = IntelAggregator.BuildResidents(kills, Now, Lookback.TwoWeeks);
        Assert.Equal(2, residents.Count);
        Assert.Equal(98000001, residents[0].CorpId);
        Assert.Equal(2, residents[0].Kills);
        Assert.Equal(3, residents[0].PilotsSeen); // pilots 1, 2, 3
        Assert.Equal(1, residents[1].Kills);
    }

    [Fact]
    public void BuildResidents_FiltersNpcCorps_AndOldKills()
    {
        var kills = new[]
        {
            Kill(Now.AddHours(-1), [1000125]),                  // NPC corp — excluded
            Kill(Now.AddDays(-20), [98000001]),                 // outside 2-week lookback
        };
        var residents = IntelAggregator.BuildResidents(kills, Now, Lookback.TwoWeeks);
        Assert.Empty(residents);
    }

    [Fact]
    public void BuildResidents_CorpAppearsOncePerKillmail()
    {
        // Two attackers from the same corp on one kill: counts as 1 kill, 2 pilots.
        var kills = new[] { Kill(Now.AddHours(-1), [98000001, 98000001], [1, 2]) };
        var residents = IntelAggregator.BuildResidents(kills, Now, Lookback.TwoWeeks);
        var top = Assert.Single(residents);
        Assert.Equal(1, top.Kills);
        Assert.Equal(2, top.PilotsSeen);
    }

    [Theory]
    [InlineData(17, "EU")]
    [InlineData(23, "US")]
    [InlineData(2, "US")]
    [InlineData(7, "AU")]
    [InlineData(12, "RU")]
    public void ClassifyTimezone_MapsPeakHour(int utcHour, string expected)
    {
        var histogram = new int[24];
        histogram[utcHour] = 10;
        Assert.Equal(expected, IntelAggregator.ClassifyTimezone(histogram));
    }

    [Fact]
    public void ClassifyTimezone_CloseSecondBucket_YieldsCombo()
    {
        var histogram = new int[24];
        histogram[17] = 10; // EU
        histogram[23] = 9;  // US — within 70%
        Assert.Equal("EU/US", IntelAggregator.ClassifyTimezone(histogram));
    }

    [Theory]
    [InlineData(5, "now")]
    [InlineData(40, "40m ago")]
    [InlineData(60 * 5, "5h ago")]
    [InlineData(60 * 72, "3d ago")]
    public void FormatLastSeen_Buckets(int minutesAgo, string expected) =>
        Assert.Equal(expected, IntelAggregator.FormatLastSeen(Now.AddMinutes(-minutesAgo), Now));

    [Fact]
    public void CountAnalyzed_RespectsLookback()
    {
        var kills = new[] { Kill(Now.AddHours(-1)), Kill(Now.AddDays(-10)), Kill(Now.AddDays(-20)) };
        Assert.Equal(2, IntelAggregator.CountAnalyzed(kills, Now, Lookback.TwoWeeks));
        Assert.Equal(1, IntelAggregator.CountAnalyzed(kills, Now, Lookback.FortyEightHours));
        Assert.Equal(3, IntelAggregator.CountAnalyzed(kills, Now, Lookback.ThreeMonths));
    }
}
