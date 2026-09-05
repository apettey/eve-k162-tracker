using System.Text.Json;
using K162.Core;
using K162.Core.Caching;
using Xunit;

namespace K162.Tests;

public class KillmailStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static KillmailDetail Kill(long id, DateTimeOffset time) =>
        new(id, time, 31000001, 587, 98000010, [98000001], [1001], 3);

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "k162-km-" + Guid.NewGuid().ToString("N"), "killmails.jsonl");

    [Fact]
    public void RoundTrip_PersistsAcrossInstances()
    {
        var path = TempFile();
        try
        {
            var store = new KillmailStore(path);
            store.Add(Kill(1, Now.AddHours(-1)));
            store.Add(Kill(2, Now.AddDays(-10)));

            var reloaded = new KillmailStore(path);
            Assert.Equal(2, reloaded.Count);
            Assert.True(reloaded.TryGet(1, out var k1));
            Assert.Equal(Now.AddHours(-1), k1.Time);
            Assert.Equal([98000001], k1.AttackerCorpIds);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void OldKillmailsSurviveRestart_NoRefetchLoop()
    {
        // Quiet wormholes list years-old kills on zkill page 1; the store must keep them.
        var path = TempFile();
        try
        {
            var store = new KillmailStore(path);
            store.Add(Kill(1, Now.AddDays(-900)));
            var reloaded = new KillmailStore(path);
            Assert.True(reloaded.TryGet(1, out _));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Load_EnforcesSizeCap_DroppingOldestKills()
    {
        var path = TempFile();
        try
        {
            var store = new KillmailStore(path);
            for (var i = 1; i <= 10; i++)
                store.Add(Kill(i, Now.AddDays(-i))); // kill 10 is oldest

            var reloaded = new KillmailStore(path, maxEntries: 4);
            Assert.Equal(4, reloaded.Count);
            Assert.True(reloaded.TryGet(1, out _));  // newest kept
            Assert.False(reloaded.TryGet(10, out _)); // oldest dropped
            Assert.False(reloaded.TryGet(5, out _));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Load_SkipsCorruptLines()
    {
        var path = TempFile();
        try
        {
            var store = new KillmailStore(path);
            store.Add(Kill(1, Now.AddHours(-1)));
            File.AppendAllText(path, "{torn json line\n");
            store.Add(Kill(2, Now.AddHours(-2)));
            // reopen: a fresh writer appends after the torn line too
            var store2 = new KillmailStore(path);
            store2.Add(Kill(3, Now.AddHours(-3)));

            var reloaded = new KillmailStore(path);
            Assert.Equal(3, reloaded.Count);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        var store = new KillmailStore();
        store.Add(Kill(1, Now));
        store.Add(Kill(1, Now.AddHours(-9))); // duplicate id ignored
        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(1, out var k));
        Assert.Equal(Now, k.Time);
    }
}

public class NameCacheSnapshotTests
{
    [Fact]
    public void RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "k162-names-" + Guid.NewGuid().ToString("N"), "names.json");
        try
        {
            var snap = new NameCacheSnapshot
            {
                Systems = new() { [30000142] = "Jita" },
                Types = new() { [587] = "Rifter" },
                Corps = new() { [98000001] = ["Cold Static", "CSTAT"] },
            };
            snap.SaveTo(path);
            var loaded = NameCacheSnapshot.LoadFrom(path);
            Assert.NotNull(loaded);
            Assert.Equal("Jita", loaded.Systems[30000142]);
            Assert.Equal("Rifter", loaded.Types[587]);
            Assert.Equal(["Cold Static", "CSTAT"], loaded.Corps[98000001]);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void LoadFrom_MissingOrCorrupt_ReturnsNull()
    {
        Assert.Null(NameCacheSnapshot.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json")));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not json");
            Assert.Null(NameCacheSnapshot.LoadFrom(path));
        }
        finally { File.Delete(path); }
    }
}

public class SystemIntelSerializationTests
{
    [Fact]
    public void SystemIntel_JsonRoundTrips()
    {
        var intel = new SystemIntel
        {
            SolarSystemId = 31000005,
            SystemName = "Thera",
            SystemClass = "THERA",
            Effect = "",
            Statics = [new StaticConnection("E587", "C2", 750_000_000)],
            KillBins = [.. Enumerable.Range(0, 24)],
            RecentKills = [new KillRecord(123, DateTimeOffset.UtcNow, "Astero", 98000001, "Corp", 5)],
            Residents = [new ResidentCorp(98000001, "Signal Cartel", "1SIG", 88, 14, "ALL", DateTimeOffset.UtcNow)],
            KillsAnalyzed = 42,
        };
        var json = JsonSerializer.Serialize(new Dictionary<int, SystemIntel> { [intel.SolarSystemId] = intel });
        var loaded = JsonSerializer.Deserialize<Dictionary<int, SystemIntel>>(json);
        Assert.NotNull(loaded);
        var round = loaded[31000005];
        Assert.Equal("Thera", round.SystemName);
        Assert.Equal(24, round.KillBins.Length);
        Assert.Equal("E587", Assert.Single(round.Statics).Code);
        Assert.Equal("750M", Assert.Single(round.Statics).MassLabel);
        Assert.Equal(5, Assert.Single(round.RecentKills).AttackerCount);
        Assert.Equal("1SIG", Assert.Single(round.Residents).Ticker);
    }
}
