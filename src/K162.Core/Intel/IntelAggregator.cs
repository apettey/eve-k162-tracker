namespace K162.Core.Intel;

/// <summary>
/// Pure aggregation of killmail details into system intel: 48h activity bins,
/// recent-kill rows, resident-corp ranking and threat level. No I/O — unit tested.
/// </summary>
public static class IntelAggregator
{
    /// <summary>Player corp ids start well above the NPC corp range; filters rat/NPC "attackers".</summary>
    public const long MinPlayerCorpId = 2_000_000;

    public const int BinCount = 24;
    public static readonly TimeSpan BinSize = TimeSpan.FromHours(2);

    /// <summary>Kills per 2h bin over the last 48h. Index 0 = oldest, 23 = most recent.</summary>
    public static int[] BuildKillBins(IEnumerable<KillmailDetail> kills, DateTimeOffset now)
    {
        var bins = new int[BinCount];
        var start = now - TimeSpan.FromHours(48);
        foreach (var k in kills)
        {
            if (k.Time < start || k.Time > now) continue;
            var idx = (int)((k.Time - start) / BinSize);
            bins[Math.Clamp(idx, 0, BinCount - 1)]++;
        }
        return bins;
    }

    /// <summary>Threat from kills in the last 6h (3 newest bins): ≥6 hot, ≥2 warm, else calm.</summary>
    public static ThreatLevel ComputeThreat(int[] bins)
    {
        var recent = bins[^3..].Sum();
        return recent >= 6 ? ThreatLevel.Hot : recent >= 2 ? ThreatLevel.Warm : ThreatLevel.Calm;
    }

    /// <summary>
    /// Ranks player corporations by killmail participation (as attackers) within the lookback
    /// window — the "who lives here" heuristic. Names/tickers are filled in by the caller.
    /// </summary>
    public static List<ResidentSummary> BuildResidents(
        IEnumerable<KillmailDetail> kills, DateTimeOffset now, Lookback lookback, int top = 6)
    {
        var cutoff = now - lookback.ToTimeSpan();
        var perCorp = new Dictionary<long, CorpAccumulator>();
        foreach (var k in kills)
        {
            if (k.Time < cutoff) continue;
            // Pair attacker corp/character where possible; corp list may be longer (NPCs without characters).
            var seenCorpsThisKill = new HashSet<long>();
            for (var i = 0; i < k.AttackerCorpIds.Count; i++)
            {
                var corpId = k.AttackerCorpIds[i];
                if (corpId < MinPlayerCorpId) continue;
                if (!perCorp.TryGetValue(corpId, out var acc))
                    perCorp[corpId] = acc = new CorpAccumulator();
                if (seenCorpsThisKill.Add(corpId)) acc.Kills++;
                if (k.Time > acc.LastSeen) acc.LastSeen = k.Time;
                acc.HourHistogram[k.Time.UtcDateTime.Hour]++;
            }
            foreach (var charId in k.AttackerCharacterIds)
            {
                // Attribute pilots to every player corp on the kill (ESI doesn't pair them here);
                // over many kills the counts converge on reality.
                foreach (var corpId in k.AttackerCorpIds)
                    if (corpId >= MinPlayerCorpId && perCorp.TryGetValue(corpId, out var acc))
                        acc.Pilots.Add(charId);
            }
        }
        return perCorp
            .OrderByDescending(kv => kv.Value.Kills)
            .ThenByDescending(kv => kv.Value.LastSeen)
            .Take(top)
            .Select(kv => new ResidentSummary(
                kv.Key, kv.Value.Pilots.Count, kv.Value.Kills,
                ClassifyTimezone(kv.Value.HourHistogram), kv.Value.LastSeen))
            .ToList();
    }

    /// <summary>
    /// Maps the UTC-hour kill histogram to an EVE timezone label. Buckets: EU 15–20,
    /// US 21–04, AU 05–09, RU 10–14. Two buckets within 70% of each other → "EU/US" style combo.
    /// </summary>
    public static string ClassifyTimezone(int[] hourHistogram)
    {
        if (hourHistogram.Sum() == 0) return "?";
        var buckets = new Dictionary<string, int> { ["EU"] = 0, ["US"] = 0, ["AU"] = 0, ["RU"] = 0 };
        for (var h = 0; h < 24; h++)
        {
            var label = h switch
            {
                >= 15 and <= 20 => "EU",
                >= 21 or <= 4 => "US",
                >= 5 and <= 9 => "AU",
                _ => "RU",
            };
            buckets[label] += hourHistogram[h];
        }
        var ranked = buckets.Where(b => b.Value > 0).OrderByDescending(b => b.Value).ToList();
        if (ranked.Count >= 2 && ranked[1].Value >= ranked[0].Value * 0.7)
            return ranked[0].Key + "/" + ranked[1].Key;
        return ranked[0].Key;
    }

    /// <summary>"now" under 10 min, then "37m ago" / "5h ago" / "3d ago".</summary>
    public static string FormatLastSeen(DateTimeOffset lastSeen, DateTimeOffset now)
    {
        var age = now - lastSeen;
        if (age < TimeSpan.FromMinutes(10)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m ago";
        if (age < TimeSpan.FromHours(48)) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    public static int CountAnalyzed(IEnumerable<KillmailDetail> kills, DateTimeOffset now, Lookback lookback)
    {
        var cutoff = now - lookback.ToTimeSpan();
        return kills.Count(k => k.Time >= cutoff);
    }

    private sealed class CorpAccumulator
    {
        public int Kills;
        public DateTimeOffset LastSeen = DateTimeOffset.MinValue;
        public HashSet<long> Pilots = [];
        public int[] HourHistogram = new int[24];
    }
}

/// <summary>Resident ranking result before corp names/tickers are resolved.</summary>
public sealed record ResidentSummary(long CorpId, int PilotsSeen, int Kills, string ActiveTimezone, DateTimeOffset LastSeen);
