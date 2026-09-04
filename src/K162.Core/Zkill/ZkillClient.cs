using System.Text.Json;

namespace K162.Core.Zkill;

public sealed record KillRef(long KillmailId, string Hash);

/// <summary>
/// zKillboard history API. Returns killmail id + hash references; details come from ESI.
/// zKillboard asks for ~1 request/second — calls are serialized through a gate.
/// </summary>
public sealed class ZkillClient(HttpClient http)
{
    private const string Base = "https://zkillboard.com/api";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    /// <summary>Pages fetched per lookback window; each page is up to 200 kills, newest first.</summary>
    public static int PagesFor(Lookback lookback) => lookback switch
    {
        Lookback.FortyEightHours => 1,
        Lookback.TwoWeeks => 2,
        _ => 3,
    };

    public async Task<IReadOnlyList<KillRef>> GetSystemKillRefsAsync(int systemId, Lookback lookback, CancellationToken ct)
    {
        var refs = new List<KillRef>();
        for (var page = 1; page <= PagesFor(lookback); page++)
        {
            var pageRefs = await GetPageAsync($"{Base}/kills/systemID/{systemId}/page/{page}/", ct);
            refs.AddRange(pageRefs);
            if (pageRefs.Count < 200) break; // last page
        }
        return refs;
    }

    private async Task<IReadOnlyList<KillRef>> GetPageAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + TimeSpan.FromSeconds(1.1) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastRequest = DateTimeOffset.UtcNow;

            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<KillRef>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("killmail_id", out var id) &&
                    el.TryGetProperty("zkb", out var zkb) &&
                    zkb.TryGetProperty("hash", out var hash))
                    list.Add(new KillRef(id.GetInt64(), hash.GetString() ?? ""));
            }
            return list;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }
}
