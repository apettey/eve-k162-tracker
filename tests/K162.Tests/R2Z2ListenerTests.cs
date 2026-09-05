using System.Collections.Concurrent;
using K162.Core.Zkill;
using Xunit;

namespace K162.Tests;

public class R2Z2ListenerTests
{
    private static string KillJson(long id, int systemId, string time = "2026-09-05T00:11:37Z") => $$$"""
        {"killmail_id":{{{id}}},"hash":"abc","esi":{"killmail_id":{{{id}}},"killmail_time":"{{{time}}}",
        "solar_system_id":{{{systemId}}},"victim":{"ship_type_id":587,"corporation_id":98000010},
        "attackers":[{"ship_type_id":17738},{"ship_type_id":11987}]},"zkb":{"hash":"abc"}}
        """;

    [Fact]
    public async Task WalksSequence_EmitsKills_StopsAt404()
    {
        var responses = new Dictionary<string, (int, string?)>
        {
            ["sequence.json"] = (200, """{"sequence":100}"""),
            ["101.json"] = (200, KillJson(9001, 31000001)),
            ["102.json"] = (200, KillJson(9002, 30000142)),
            ["103.json"] = (404, null),
        };
        var listener = new R2Z2Listener((path, _) =>
            Task.FromResult(responses.TryGetValue(path, out var r) ? r : (404, (string?)null)));

        var kills = new ConcurrentQueue<LiveKill>();
        var listening = new ConcurrentQueue<bool>();
        listener.KillReceived += kills.Enqueue;
        listener.ListeningChanged += listening.Enqueue;

        using var cts = new CancellationTokenSource();
        var run = listener.RunAsync(cts.Token);
        var deadline = Environment.TickCount64 + 10000;
        while (kills.Count < 2 && Environment.TickCount64 < deadline) await Task.Delay(50);
        cts.Cancel();
        await run;

        Assert.Equal(2, kills.Count);
        Assert.True(kills.TryDequeue(out var first));
        Assert.Equal(9001, first.KillmailId);
        Assert.Equal(31000001, first.SolarSystemId);
        Assert.Equal(587, first.VictimShipTypeId);
        Assert.Equal(98000010, first.VictimCorpId);
        Assert.Equal(2, first.AttackerCount);
        Assert.Contains(true, listening);
    }

    [Fact]
    public async Task MalformedPackage_DoesNotKillTheFeed()
    {
        var responses = new Dictionary<string, (int, string?)>
        {
            ["sequence.json"] = (200, """{"sequence":100}"""),
            ["101.json"] = (200, "{broken json"),
            ["102.json"] = (200, KillJson(9002, 30000142)),
            ["103.json"] = (404, null),
        };
        var listener = new R2Z2Listener((path, _) =>
            Task.FromResult(responses.TryGetValue(path, out var r) ? r : (404, (string?)null)));

        var kills = new ConcurrentQueue<LiveKill>();
        listener.KillReceived += kills.Enqueue;
        using var cts = new CancellationTokenSource();
        var run = listener.RunAsync(cts.Token);
        var deadline = Environment.TickCount64 + 10000;
        while (kills.IsEmpty && Environment.TickCount64 < deadline) await Task.Delay(50);
        cts.Cancel();
        await run;

        Assert.True(kills.TryDequeue(out var kill));
        Assert.Equal(9002, kill.KillmailId);
    }

    [Fact]
    public async Task SequenceFetchFailure_ReportsNotListening_AndRetries()
    {
        var attempts = 0;
        var listener = new R2Z2Listener((path, _) =>
        {
            if (path == "sequence.json" && Interlocked.Increment(ref attempts) >= 2)
                return Task.FromResult((200, (string?)"""{"sequence":100}"""));
            return Task.FromResult((500, (string?)null));
        });
        var listening = new ConcurrentQueue<bool>();
        listener.ListeningChanged += listening.Enqueue;

        using var cts = new CancellationTokenSource();
        var run = listener.RunAsync(cts.Token);
        var deadline = Environment.TickCount64 + 15000;
        while (!listening.Contains(true) && Environment.TickCount64 < deadline) await Task.Delay(100);
        cts.Cancel();
        await run;

        Assert.Contains(false, listening); // reported the outage
        Assert.Contains(true, listening);  // then recovered
    }
}
