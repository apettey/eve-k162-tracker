using System.Text.Json;

namespace K162.Core;

/// <summary>
/// Bundled J-space database (generated from anoik.is static data at build time):
/// system name, class, effect and static connections keyed by solar system id.
/// </summary>
public sealed class WormholeDb
{
    private readonly Dictionary<int, Entry> _byId = [];
    private readonly Dictionary<string, int> _idByName = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Entry(string Name, string RawClass, string Effect, IReadOnlyList<StaticConnection> Statics);

    public static WormholeDb LoadFromFile(string path) => LoadFromJson(File.ReadAllText(path));

    public static WormholeDb LoadFromJson(string json)
    {
        var db = new WormholeDb();
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var id = int.Parse(prop.Name);
            var v = prop.Value;
            var name = v.GetProperty("n").GetString() ?? "";
            var cls = v.GetProperty("c").GetString() ?? "";
            var effect = v.TryGetProperty("e", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            var statics = new List<StaticConnection>();
            if (v.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Array)
            {
                foreach (var st in s.EnumerateArray())
                {
                    statics.Add(new StaticConnection(
                        st.GetProperty("k").GetString() ?? "?",
                        FormatClass(st.GetProperty("d").GetString() ?? "?"),
                        st.GetProperty("m").GetInt64()));
                }
            }
            db._byId[id] = new Entry(name, cls, effect, statics);
            db._idByName[name] = id;
        }
        return db;
    }

    public Entry? Find(int solarSystemId) => _byId.TryGetValue(solarSystemId, out var e) ? e : null;

    public Entry? FindByName(string systemName) =>
        _idByName.TryGetValue(systemName, out var id) ? _byId[id] : null;

    public bool TryGetId(string systemName, out int solarSystemId) =>
        _idByName.TryGetValue(systemName, out solarSystemId);

    public bool IsWormholeSystem(int solarSystemId) => _byId.ContainsKey(solarSystemId);

    public int Count => _byId.Count;

    /// <summary>Maps a raw anoik.is class code to the display label used across the UI.</summary>
    public static string FormatClass(string raw) => raw.ToLowerInvariant() switch
    {
        "" or "?" => "?",
        "hs" => "HS",
        "ls" => "LS",
        "ns" => "NS",
        "thera" => "THERA",
        "c13" => "C13 SHATTERED",
        "sentinel" => "DRIFTER SENTINEL",
        "barbican" => "DRIFTER BARBICAN",
        "vidette" => "DRIFTER VIDETTE",
        "conflux" => "DRIFTER CONFLUX",
        "redoubt" => "DRIFTER REDOUBT",
        var c when c.StartsWith('c') && c.Length <= 3 => c.ToUpperInvariant(),
        var c => c.ToUpperInvariant(),
    };
}
