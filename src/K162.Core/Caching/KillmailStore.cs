using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace K162.Core.Caching;

/// <summary>
/// Disk-backed cache of killmail details. Killmails are immutable, so once fetched from
/// ESI they never need fetching again — this is the single biggest cold-start saver
/// (an intel refresh can need hundreds of killmail lookups per system).
///
/// Format: JSON Lines append-only file. Pruning is by SIZE CAP (oldest kills dropped),
/// never by kill age alone: quiet wormholes list years-old kills on zKillboard's first
/// page, and age-pruning those would refetch + re-drop them on every restart.
/// </summary>
public sealed class KillmailStore
{
    public const int MaxEntries = 200_000; // ≈40-50MB on disk — years of WH traffic
    private const double CompactionThreshold = 0.2;

    private readonly ConcurrentDictionary<long, KillmailDetail> _byId = [];
    private readonly string? _path;
    private readonly Lock _writeLock = new();

    /// <summary>In-memory only (tests / fallback).</summary>
    public KillmailStore() { }

    /// <summary>Loads the store from disk; unreadable lines are skipped, size cap enforced.</summary>
    public KillmailStore(string path, int maxEntries = MaxEntries)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) return;

        var total = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                total++;
                try
                {
                    var detail = JsonSerializer.Deserialize<KillmailDetail>(line);
                    if (detail is not null)
                        _byId[detail.KillmailId] = detail;
                }
                catch (JsonException)
                {
                    // torn/corrupt line (e.g. crash mid-append) — skip
                }
            }
        }
        catch (IOException)
        {
            return; // unreadable file — run with what we got
        }

        var over = _byId.Count - maxEntries;
        if (over > 0)
            foreach (var victim in _byId.Values.OrderBy(k => k.Time).Take(over))
                _byId.TryRemove(victim.KillmailId, out _);

        // Rewrite when the file carries meaningful dead weight (dropped or duplicate lines).
        if (total > 100 && _byId.Count < total * (1 - CompactionThreshold))
            Compact();
    }

    public int Count => _byId.Count;

    public bool TryGet(long killmailId, out KillmailDetail detail)
    {
        if (_byId.TryGetValue(killmailId, out var found)) { detail = found; return true; }
        detail = null!;
        return false;
    }

    /// <summary>Adds and appends to disk. Already-known killmails are ignored (idempotent).</summary>
    public void Add(KillmailDetail detail)
    {
        if (!_byId.TryAdd(detail.KillmailId, detail)) return;
        if (_path is null) return;
        var line = JsonSerializer.Serialize(detail) + Environment.NewLine;
        lock (_writeLock)
        {
            try { File.AppendAllText(_path, line, Encoding.UTF8); }
            catch (IOException) { /* disk hiccup — entry stays in memory */ }
        }
    }

    private void Compact()
    {
        if (_path is null) return;
        lock (_writeLock)
        {
            try
            {
                var tmp = _path + ".tmp";
                using (var w = new StreamWriter(tmp, append: false, Encoding.UTF8))
                    foreach (var detail in _byId.Values)
                        w.WriteLine(JsonSerializer.Serialize(detail));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (IOException) { /* keep the old file */ }
        }
    }
}
