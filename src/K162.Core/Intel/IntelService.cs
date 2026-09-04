using System.Collections.Concurrent;
using K162.Core.Esi;
using K162.Core.Zkill;

namespace K162.Core.Intel;

/// <summary>
/// Builds and caches per-system intel: wormhole metadata from the bundled db,
/// kill history from zKillboard refs resolved through ESI, aggregated by IntelAggregator.
/// </summary>
public sealed class IntelService(EsiClient esi, ZkillClient zkill, WormholeDb wormholes)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const int KillmailFetchConcurrency = 8;

    private readonly ConcurrentDictionary<int, SystemIntel> _cache = [];
    private readonly ConcurrentDictionary<long, KillmailDetail?> _killmails = [];

    /// <summary>Drops cached intel (e.g. when the lookback setting changes).</summary>
    public void ClearCache() => _cache.Clear();

    public SystemIntel? GetCached(int systemId) =>
        _cache.TryGetValue(systemId, out var intel) && DateTimeOffset.UtcNow - intel.FetchedAt < CacheTtl
            ? intel : null;

    public async Task<SystemIntel> GetIntelAsync(int systemId, Lookback lookback, CancellationToken ct)
    {
        if (GetCached(systemId) is { } cached) return cached;

        var now = DateTimeOffset.UtcNow;
        var wh = wormholes.Find(systemId);
        var systemName = wh?.Name ?? await esi.GetSystemNameAsync(systemId, ct) ?? $"System {systemId}";

        var refs = await zkill.GetSystemKillRefsAsync(systemId, lookback, ct);
        var details = await FetchKillmailsAsync(refs, ct);
        var cutoff = now - lookback.ToTimeSpan();
        var inWindow = details.Where(k => k.Time >= cutoff).ToList();

        var bins = IntelAggregator.BuildKillBins(inWindow, now);
        var residentSummaries = IntelAggregator.BuildResidents(inWindow, now, lookback);

        // Resolve display names: victim ship types + victim corps (bulk), resident corps (ticker needs /corporations/).
        var recent48 = inWindow.Where(k => k.Time >= now - TimeSpan.FromHours(48))
            .OrderByDescending(k => k.Time).Take(10).ToList();
        var nameIds = recent48.SelectMany(k => new[] { (long)k.VictimShipTypeId, k.VictimCorpId })
            .Where(id => id > 0).ToList();
        var names = nameIds.Count > 0 ? await esi.ResolveNamesAsync(nameIds, ct) : [];

        var recentKills = recent48.Select(k => new KillRecord(
            k.KillmailId, k.Time,
            names.GetValueOrDefault(k.VictimShipTypeId, $"Type {k.VictimShipTypeId}"),
            k.VictimCorpId,
            names.GetValueOrDefault(k.VictimCorpId, ""),
            k.AttackerCount)).ToList();

        var residents = new List<ResidentCorp>();
        foreach (var r in residentSummaries)
        {
            var corp = await esi.GetCorporationAsync(r.CorpId, ct);
            residents.Add(new ResidentCorp(
                r.CorpId, corp?.Name ?? $"Corp {r.CorpId}", corp?.Ticker ?? "?",
                r.PilotsSeen, r.Kills, r.ActiveTimezone, r.LastSeen));
        }

        var intel = new SystemIntel
        {
            SolarSystemId = systemId,
            SystemName = systemName,
            SystemClass = wh is null ? "" : WormholeDb.FormatClass(wh.RawClass),
            Effect = wh?.Effect ?? "",
            Statics = wh?.Statics ?? [],
            KillBins = bins,
            RecentKills = recentKills,
            Residents = residents,
            KillsAnalyzed = IntelAggregator.CountAnalyzed(inWindow, now, lookback),
            FetchedAt = now,
        };
        _cache[systemId] = intel;
        return intel;
    }

    /// <summary>Folds a live RedisQ kill into the cached intel for its system, if cached.</summary>
    public SystemIntel? ApplyLiveKill(LiveKill kill, string victimShipName, string victimCorpName)
    {
        if (!_cache.TryGetValue(kill.SolarSystemId, out var intel)) return null;
        var now = DateTimeOffset.UtcNow;
        var bins = IntelAggregator.BuildKillBins(
            intel.RecentKills.Select(k => new KillmailDetail(k.KillmailId, k.Time, kill.SolarSystemId, 0, 0, [], [], k.AttackerCount))
                .Append(new KillmailDetail(kill.KillmailId, kill.Time, kill.SolarSystemId, 0, 0, [], [], kill.AttackerCount)),
            now);
        // Preserve older-bin history the recent-kill list no longer covers.
        for (var i = 0; i < bins.Length; i++) bins[i] = Math.Max(bins[i], intel.KillBins[i]);

        var updated = new SystemIntel
        {
            SolarSystemId = intel.SolarSystemId,
            SystemName = intel.SystemName,
            SystemClass = intel.SystemClass,
            Effect = intel.Effect,
            Statics = intel.Statics,
            KillBins = bins,
            RecentKills = intel.RecentKills
                .Prepend(new KillRecord(kill.KillmailId, kill.Time, victimShipName, kill.VictimCorpId, victimCorpName, kill.AttackerCount))
                .Take(10).ToList(),
            Residents = intel.Residents,
            KillsAnalyzed = intel.KillsAnalyzed + 1,
            FetchedAt = intel.FetchedAt,
        };
        _cache[kill.SolarSystemId] = updated;
        return updated;
    }

    private async Task<List<KillmailDetail>> FetchKillmailsAsync(IReadOnlyList<KillRef> refs, CancellationToken ct)
    {
        var results = new ConcurrentBag<KillmailDetail>();
        await Parallel.ForEachAsync(refs,
            new ParallelOptions { MaxDegreeOfParallelism = KillmailFetchConcurrency, CancellationToken = ct },
            async (r, token) =>
            {
                if (!_killmails.TryGetValue(r.KillmailId, out var detail))
                {
                    try { detail = await esi.GetKillmailAsync(r.KillmailId, r.Hash, token); }
                    catch (Exception) { detail = null; }
                    _killmails[r.KillmailId] = detail;
                }
                if (detail is not null) results.Add(detail);
            });
        return [.. results];
    }
}
