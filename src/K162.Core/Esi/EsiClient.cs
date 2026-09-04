using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace K162.Core.Esi;

public sealed record EsiShip(int ShipTypeId, string ShipName);
public sealed record EsiCorporation(string Name, string Ticker);

/// <summary>Thin ESI client with in-memory caches for immutable lookups (names, types, corps).</summary>
public sealed class EsiClient(HttpClient http)
{
    private const string Base = "https://esi.evetech.net/latest";
    private readonly ConcurrentDictionary<int, string> _systemNames = [];
    private readonly ConcurrentDictionary<int, string> _typeNames = [];
    private readonly ConcurrentDictionary<long, EsiCorporation> _corps = [];

    private async Task<JsonDocument?> GetAsync(string path, string? accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Base + path);
        if (accessToken is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new EsiUnauthorizedException();
        if (!res.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    public async Task<int?> GetLocationAsync(long characterId, string accessToken, CancellationToken ct)
    {
        using var doc = await GetAsync($"/characters/{characterId}/location/", accessToken, ct);
        return doc?.RootElement.GetProperty("solar_system_id").GetInt32();
    }

    public async Task<bool?> GetOnlineAsync(long characterId, string accessToken, CancellationToken ct)
    {
        using var doc = await GetAsync($"/characters/{characterId}/online/", accessToken, ct);
        return doc?.RootElement.GetProperty("online").GetBoolean();
    }

    public async Task<EsiShip?> GetShipAsync(long characterId, string accessToken, CancellationToken ct)
    {
        using var doc = await GetAsync($"/characters/{characterId}/ship/", accessToken, ct);
        if (doc is null) return null;
        var typeId = doc.RootElement.GetProperty("ship_type_id").GetInt32();
        return new EsiShip(typeId, await GetTypeNameAsync(typeId, ct) ?? $"Type {typeId}");
    }

    public async Task<long?> GetCharacterCorpIdAsync(long characterId, CancellationToken ct)
    {
        using var doc = await GetAsync($"/characters/{characterId}/", null, ct);
        return doc?.RootElement.GetProperty("corporation_id").GetInt64();
    }

    public async Task<EsiCorporation?> GetCorporationAsync(long corpId, CancellationToken ct)
    {
        if (_corps.TryGetValue(corpId, out var cached)) return cached;
        using var doc = await GetAsync($"/corporations/{corpId}/", null, ct);
        if (doc is null) return null;
        var corp = new EsiCorporation(
            doc.RootElement.GetProperty("name").GetString() ?? "",
            doc.RootElement.GetProperty("ticker").GetString() ?? "");
        _corps[corpId] = corp;
        return corp;
    }

    public async Task<string?> GetSystemNameAsync(int systemId, CancellationToken ct)
    {
        if (_systemNames.TryGetValue(systemId, out var cached)) return cached;
        using var doc = await GetAsync($"/universe/systems/{systemId}/", null, ct);
        var name = doc?.RootElement.GetProperty("name").GetString();
        if (name is not null) _systemNames[systemId] = name;
        return name;
    }

    public async Task<string?> GetTypeNameAsync(int typeId, CancellationToken ct)
    {
        if (_typeNames.TryGetValue(typeId, out var cached)) return cached;
        using var doc = await GetAsync($"/universe/types/{typeId}/", null, ct);
        var name = doc?.RootElement.GetProperty("name").GetString();
        if (name is not null) _typeNames[typeId] = name;
        return name;
    }

    /// <summary>Bulk id → name resolution via POST /universe/names/ (max 1000 ids). Caches type names.</summary>
    public async Task<Dictionary<long, string>> ResolveNamesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        var distinct = ids.Distinct().ToList();
        foreach (var chunk in distinct.Chunk(1000))
        {
            using var res = await http.PostAsJsonAsync(Base + "/universe/names/", chunk, ct);
            if (!res.IsSuccessStatusCode) continue;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = el.GetProperty("id").GetInt64();
                var name = el.GetProperty("name").GetString() ?? "";
                result[id] = name;
                if (el.GetProperty("category").GetString() == "inventory_type")
                    _typeNames[(int)id] = name;
            }
        }
        return result;
    }

    /// <summary>Resolves a solar system name to its id via POST /universe/ids/ (cached).</summary>
    public async Task<int?> ResolveSystemIdAsync(string systemName, CancellationToken ct)
    {
        var cached = _systemNames.FirstOrDefault(kv =>
            string.Equals(kv.Value, systemName, StringComparison.OrdinalIgnoreCase));
        if (cached.Key != 0) return cached.Key;

        using var res = await http.PostAsJsonAsync(Base + "/universe/ids/", new[] { systemName }, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("systems", out var systems) ||
            systems.ValueKind != JsonValueKind.Array) return null;
        foreach (var el in systems.EnumerateArray())
        {
            if (string.Equals(el.GetProperty("name").GetString(), systemName, StringComparison.OrdinalIgnoreCase))
            {
                var id = el.GetProperty("id").GetInt32();
                _systemNames[id] = el.GetProperty("name").GetString()!;
                return id;
            }
        }
        return null;
    }

    public async Task<KillmailDetail?> GetKillmailAsync(long killmailId, string hash, CancellationToken ct)
    {
        using var doc = await GetAsync($"/killmails/{killmailId}/{hash}/", null, ct);
        if (doc is null) return null;
        var root = doc.RootElement;
        var victim = root.GetProperty("victim");
        var attackers = root.GetProperty("attackers");
        var corpIds = new List<long>();
        var charIds = new List<long>();
        foreach (var a in attackers.EnumerateArray())
        {
            if (a.TryGetProperty("corporation_id", out var c)) corpIds.Add(c.GetInt64());
            if (a.TryGetProperty("character_id", out var ch)) charIds.Add(ch.GetInt64());
        }
        return new KillmailDetail(
            killmailId,
            root.GetProperty("killmail_time").GetDateTimeOffset(),
            root.GetProperty("solar_system_id").GetInt32(),
            victim.TryGetProperty("ship_type_id", out var st) ? st.GetInt32() : 0,
            victim.TryGetProperty("corporation_id", out var vc) ? vc.GetInt64() : 0,
            corpIds, charIds, attackers.GetArrayLength());
    }
}

public sealed class EsiUnauthorizedException : Exception;
