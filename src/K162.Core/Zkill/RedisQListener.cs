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
/// Long-polls the zKillboard RedisQ live killmail stream and raises an event per kill.
/// The queue id is random per install so we get our own cursor into the stream.
/// </summary>
public sealed class RedisQListener(HttpClient http)
{
    private const string Endpoint = "https://zkillboard.com/api/redisq.php";
    private readonly string _queueId = "k162-" + Guid.NewGuid().ToString("N")[..12];

    public event Action<LiveKill>? KillReceived;
    public event Action<bool>? ListeningChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var res = await http.GetAsync($"{Endpoint}?queueID={_queueId}&ttw=10", ct);
                if (!res.IsSuccessStatusCode)
                {
                    failures++;
                    ListeningChanged?.Invoke(false);
                    // 429 or transient failure — back off, at most 60s.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct);
                    continue;
                }
                failures = 0;
                ListeningChanged?.Invoke(true);

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("package", out var pkg) || pkg.ValueKind != JsonValueKind.Object)
                    continue; // empty poll — go straight back to listening

                var km = pkg.GetProperty("killmail");
                var victim = km.GetProperty("victim");
                KillReceived?.Invoke(new LiveKill(
                    pkg.GetProperty("killID").GetInt64(),
                    km.GetProperty("solar_system_id").GetInt32(),
                    km.GetProperty("killmail_time").GetDateTimeOffset(),
                    victim.TryGetProperty("ship_type_id", out var st) ? st.GetInt32() : 0,
                    victim.TryGetProperty("corporation_id", out var vc) ? vc.GetInt64() : 0,
                    km.GetProperty("attackers").GetArrayLength()));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failures++;
                ListeningChanged?.Invoke(false);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        ListeningChanged?.Invoke(false);
    }
}
