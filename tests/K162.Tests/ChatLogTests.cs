using System.Text;
using K162.Core.ChatLogs;
using Xunit;

namespace K162.Tests;

public class ChatLogParserTests
{
    [Fact]
    public void CharacterIdFromFileName_ModernAndLegacy()
    {
        Assert.Equal(2112625428, ChatLogParser.CharacterIdFromFileName("Local_20260905_102345_2112625428.txt"));
        Assert.Null(ChatLogParser.CharacterIdFromFileName("Local_20260905_102345.txt"));
    }

    [Fact]
    public void SessionStampFromFileName_Parses()
    {
        var stamp = ChatLogParser.SessionStampFromFileName("Local_20260905_102345_2112625428.txt");
        Assert.Equal(new DateTime(2026, 9, 5, 10, 23, 45, DateTimeKind.Utc), stamp);
        Assert.Null(ChatLogParser.SessionStampFromFileName("Local_garbage.txt"));
    }

    [Fact]
    public void ListenerFromHeader_FindsName()
    {
        const string header = """
            ---------------------------------------------------------------
              Channel ID:      local
              Channel Name:    Local
              Listener:        Vex Arkanis
              Session started: 2026.09.05 10:23:45
            ---------------------------------------------------------------
            """;
        Assert.Equal("Vex Arkanis", ChatLogParser.ListenerFromHeader(header));
        Assert.Null(ChatLogParser.ListenerFromHeader("no header here"));
    }

    [Fact]
    public void TryParseSystemChange_ParsesSystemAndTime()
    {
        var ok = ChatLogParser.TryParseSystemChange(
            "[ 2026.09.05 10:31:02 ] EVE System > Channel changed to Local : J115844",
            out var system, out var time);
        Assert.True(ok);
        Assert.Equal("J115844", system);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 10, 31, 2, TimeSpan.Zero), time);
    }

    [Theory]
    [InlineData("[ 2026.09.05 10:31:02 ] Vex Arkanis > hello local")]
    [InlineData("random noise")]
    [InlineData("")]
    public void TryParseSystemChange_RejectsChatter(string line) =>
        Assert.False(ChatLogParser.TryParseSystemChange(line, out _, out _));

    [Fact]
    public void LastSystemChange_PicksNewest()
    {
        var lines = new[]
        {
            "[ 2026.09.05 10:00:00 ] EVE System > Channel changed to Local : Jita",
            "[ 2026.09.05 10:05:00 ] Someone > chat noise",
            "[ 2026.09.05 10:31:02 ] EVE System > Channel changed to Local : J115844",
        };
        var last = ChatLogParser.LastSystemChange(lines);
        Assert.NotNull(last);
        Assert.Equal("J115844", last.Value.SystemName);
    }
}

public class ChatLogWatcherTests
{
    private static string WriteLog(string dir, string fileName, string content)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(content)).ToArray());
        return path;
    }

    [Fact]
    public async Task Watcher_ReplaysLastChange_ThenStreamsNewOnes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k162-chatlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = WriteLog(dir, "Local_20260905_102345_2112625428.txt", """
                ---------------------------------------------------------------
                  Channel ID:      local
                  Channel Name:    Local
                  Listener:        Vex Arkanis
                  Session started: 2026.09.05 10:23:45
                ---------------------------------------------------------------

                """.ReplaceLineEndings("\r\n") +
                "[ 2026.09.05 10:23:47 ] EVE System > Channel changed to Local : Jita\r\n" +
                "[ 2026.09.05 10:31:02 ] EVE System > Channel changed to Local : J115844\r\n");

            var watcher = new ChatLogWatcher(dir);
            var changes = new List<LocalSystemChange>();
            watcher.SystemChanged += c => { lock (changes) changes.Add(c); };
            using var cts = new CancellationTokenSource();
            var run = watcher.RunAsync(cts.Token);

            await WaitFor(() => { lock (changes) return changes.Count >= 1; });
            lock (changes)
            {
                // Initial replay: only the LAST historical change is emitted.
                var first = Assert.Single(changes);
                Assert.Equal("J115844", first.SystemName);
                Assert.Equal(2112625428, first.CharacterId);
            }

            // Append a live jump line (even-byte UTF-16, no BOM on append).
            await File.AppendAllTextAsync(path,
                "[ 2026.09.05 10:35:00 ] EVE System > Channel changed to Local : Thera\r\n",
                Encoding.Unicode);
            await WaitFor(() => { lock (changes) return changes.Count >= 2; });
            lock (changes) Assert.Equal("Thera", changes[^1].SystemName);

            cts.Cancel();
            await run;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Watcher_TailsOnlyNewestFilePerCharacter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k162-chatlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteLog(dir, "Local_20260904_080000_111.txt",
                "  Listener:        Old Session\r\n\r\n[ 2026.09.04 08:00:01 ] EVE System > Channel changed to Local : Amarr\r\n");
            WriteLog(dir, "Local_20260905_090000_111.txt",
                "  Listener:        Vex Arkanis\r\n\r\n[ 2026.09.05 09:00:01 ] EVE System > Channel changed to Local : Jita\r\n");

            var watcher = new ChatLogWatcher(dir);
            var changes = new List<LocalSystemChange>();
            var counts = new List<int>();
            watcher.SystemChanged += c => { lock (changes) changes.Add(c); };
            watcher.WatchingChanged += n => { lock (counts) counts.Add(n); };
            using var cts = new CancellationTokenSource();
            var run = watcher.RunAsync(cts.Token);

            await WaitFor(() => { lock (changes) return changes.Count >= 1; });
            lock (changes)
            {
                var only = Assert.Single(changes); // older session for char 111 ignored
                Assert.Equal("Jita", only.SystemName);
            }
            lock (counts) Assert.Contains(1, counts);

            cts.Cancel();
            await run;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "timed out waiting for condition");
            await Task.Delay(100);
        }
    }
}
