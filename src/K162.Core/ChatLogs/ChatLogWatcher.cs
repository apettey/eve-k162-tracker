using System.Text;

namespace K162.Core.ChatLogs;

public sealed record LocalSystemChange(long? CharacterId, string? ListenerName, string SystemName, DateTimeOffset Time);

/// <summary>
/// Tails EVE Local chat logs to detect system changes near-instantly (the client appends
/// "Channel changed to Local : X" the moment a character lands in a new system — much faster
/// than the ~5s ESI location poll). Requires "Log Chat to File" enabled in the EVE client.
///
/// One newest Local_*.txt file is tailed per character. Files are UTF-16LE and held open
/// by the game, so they are read share-friendly from a remembered offset once a second.
/// </summary>
public sealed class ChatLogWatcher(string? directory = null)
{
    private static readonly TimeSpan TailInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxFileAge = TimeSpan.FromHours(36);

    private readonly string _directory = string.IsNullOrWhiteSpace(directory)
        ? DefaultDirectory
        : directory!;
    private readonly Dictionary<string, Tail> _tails = []; // keyed by full path

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Chatlogs");

    public event Action<LocalSystemChange>? SystemChanged;
    /// <summary>Number of log files currently being tailed (0 = none found).</summary>
    public event Action<int>? WatchingChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        var lastRescan = DateTimeOffset.MinValue;
        var lastReported = -1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastRescan >= RescanInterval)
                {
                    lastRescan = DateTimeOffset.UtcNow;
                    Rescan();
                    if (_tails.Count != lastReported)
                    {
                        lastReported = _tails.Count;
                        WatchingChanged?.Invoke(_tails.Count);
                    }
                }
                foreach (var tail in _tails.Values)
                    PumpTail(tail);
            }
            catch (Exception)
            {
                // Directory vanished / transient IO — keep trying.
            }
            try { await Task.Delay(TailInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Rescan()
    {
        if (!Directory.Exists(_directory))
        {
            _tails.Clear();
            return;
        }
        var cutoff = DateTime.UtcNow - MaxFileAge;
        var candidates = new DirectoryInfo(_directory)
            .EnumerateFiles("Local_*.txt")
            .Where(f => f.LastWriteTimeUtc >= cutoff);

        // Newest file per character (id from filename when present, else per file).
        var newestPerKey = new Dictionary<string, FileInfo>();
        foreach (var f in candidates)
        {
            var key = ChatLogParser.CharacterIdFromFileName(f.Name)?.ToString() ?? f.FullName;
            var stamp = ChatLogParser.SessionStampFromFileName(f.Name) ?? f.LastWriteTimeUtc;
            if (!newestPerKey.TryGetValue(key, out var existing) ||
                stamp > (ChatLogParser.SessionStampFromFileName(existing.Name) ?? existing.LastWriteTimeUtc))
                newestPerKey[key] = f;
        }

        var wanted = newestPerKey.Values.Select(f => f.FullName).ToHashSet();
        foreach (var stale in _tails.Keys.Where(k => !wanted.Contains(k)).ToList())
            _tails.Remove(stale);
        foreach (var f in newestPerKey.Values)
            if (!_tails.ContainsKey(f.FullName))
                _tails[f.FullName] = new Tail { Path = f.FullName, CharacterId = ChatLogParser.CharacterIdFromFileName(f.Name) };
    }

    private void PumpTail(Tail tail)
    {
        long length;
        try { length = new FileInfo(tail.Path).Length; }
        catch (Exception) { return; }
        if (length <= tail.Offset) return;

        using var stream = new FileStream(tail.Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Position = tail.Offset;
        // Read an even number of bytes so UTF-16 code units stay aligned.
        var toRead = (int)Math.Min(length - tail.Offset, 1 << 20);
        toRead -= toRead % 2;
        if (toRead <= 0) return;
        var buffer = new byte[toRead];
        var read = stream.Read(buffer, 0, toRead);
        read -= read % 2;
        if (read <= 0) return;

        var isFirstRead = tail.Offset == 0;
        tail.Offset += read;
        var text = tail.Remainder + Encoding.Unicode.GetString(buffer, 0, read).TrimStart((char)0xFEFF);
        var lines = text.Split('\n');
        tail.Remainder = lines[^1];
        var complete = lines[..^1].Select(l => l.TrimEnd('\r'));

        if (isFirstRead)
        {
            // Historical replay: establish current state from the header + last change only.
            var completeList = complete.ToList();
            tail.Listener ??= ChatLogParser.ListenerFromHeader(string.Join('\n', completeList.Take(8)));
            if (ChatLogParser.LastSystemChange(completeList) is { } last)
                SystemChanged?.Invoke(new LocalSystemChange(tail.CharacterId, tail.Listener, last.SystemName, last.Time));
            return;
        }

        foreach (var line in complete)
            if (ChatLogParser.TryParseSystemChange(line, out var sys, out var time))
                SystemChanged?.Invoke(new LocalSystemChange(tail.CharacterId, tail.Listener, sys, time));
    }

    private sealed class Tail
    {
        public required string Path { get; init; }
        public long? CharacterId { get; init; }
        public string? Listener { get; set; }
        public long Offset { get; set; }
        public string Remainder { get; set; } = "";
    }
}
