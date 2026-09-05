using System.Text.Json;

namespace K162.Core.Zkill;

public sealed record LiveKill(
    long KillmailId,
    int SolarSystemId,
    DateTimeOffset Time,
    int VictimShipTypeId,
    long VictimCorpId,
    int AttackerCount);

/// <summary>
/// zKillboard R2Z2 live killmail feed (the RedisQ successor, RedisQ died 2026-05-31):
/// a pull-based sequence of JSON files. We read sequence.json for the head, then walk
/// {seq+1}.json until a 404 says we're caught up, wait ~6s, and repeat. Rate limit is
/// 15 req/s/IP with 1-hour bans (403) for abusers, so the walk is deliberately gentle.
/// </summary>
public sealed class R2Z2Listener
{
    private const string Base = "https://r2z2.zkillboard.com/ephemeral/";
    private static readonly TimeSpan CaughtUpDelay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BanDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WalkDelay = TimeSpan.FromMilliseconds(250);
    /// <summary>If we fall further behind than this, skip to the head — we feed live intel, not history.</summary>
    private const long MaxBacklog = 200;

    private readonly Func<string, CancellationToken, Task<(int Status, string? Body)>> _fetch;

    public event Action<LiveKill>? KillReceived;
    public event Action<bool>? ListeningChanged;

    public R2Z2Listener(HttpClient http)
        : this(async (url, ct) =>
        {
            using var res = await http.GetAsync(Base + url, ct);
            return ((int)res.StatusCode, res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct) : null);
        })
    {
    }

    /// <summary>Test seam: fetcher takes a path relative to the ephemeral root.</summary>
    public R2Z2Listener(Func<string, CancellationToken, Task<(int Status, string? Body)>> fetch) => _fetch = fetch;

    public async Task RunAsync(CancellationToken ct)
    {
        long sequence = 0;
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (sequence == 0)
                {
                    sequence = await FetchSequenceAsync(ct) ?? 0;
                    if (sequence == 0)
                    {
                        failures++;
                        ListeningChanged?.Invoke(false);
                        await Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct);
                        continue;
                    }
                    ListeningChanged?.Invoke(true);
                }

                var (status, body) = await _fetch.Invoke($"{sequence + 1}.json", ct);
                switch (status)
                {
                    case 200 when body is not null:
                        failures = 0;
                        sequence++;
                        ListeningChanged?.Invoke(true);
                        Emit(body);
                        await Delay(WalkDelay, ct);
                        break;

                    case 404: // caught up — idle, then resync against the head
                        failures = 0;
                        ListeningChanged?.Invoke(true);
                        await Delay(CaughtUpDelay, ct);
                        var head = await FetchSequenceAsync(ct);
                        if (head.HasValue && head.Value - sequence > MaxBacklog)
                            sequence = head.Value;
                        break;

                    case 403: // banned or blocked — long cooldown
                        ListeningChanged?.Invoke(false);
                        await Delay(BanDelay, ct);
                        sequence = 0;
                        break;

                    default:
                        failures++;
                        ListeningChanged?.Invoke(false);
                        await Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failures++;
                ListeningChanged?.Invoke(false);
                await Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct);
            }
        }
        ListeningChanged?.Invoke(false);
    }

    private async Task<long?> FetchSequenceAsync(CancellationToken ct)
    {
        var (status, body) = await _fetch.Invoke("sequence.json", ct);
        if (status != 200 || body is null) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("sequence", out var s) ? s.GetInt64() : null;
    }

    private void Emit(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("esi", out var esi)) return;
            var victim = esi.GetProperty("victim");
            KillReceived?.Invoke(new LiveKill(
                root.GetProperty("killmail_id").GetInt64(),
                esi.GetProperty("solar_system_id").GetInt32(),
                esi.GetProperty("killmail_time").GetDateTimeOffset(),
                victim.TryGetProperty("ship_type_id", out var st) ? st.GetInt32() : 0,
                victim.TryGetProperty("corporation_id", out var vc) ? vc.GetInt64() : 0,
                esi.GetProperty("attackers").GetArrayLength()));
        }
        catch (Exception)
        {
            // One malformed package must not kill the feed.
        }
    }

    private static Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
