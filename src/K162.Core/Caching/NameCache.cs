using System.Text.Json;

namespace K162.Core.Caching;

/// <summary>
/// Serializable snapshot of the ESI name caches (system/type names, corp name+ticker).
/// These are effectively immutable in EVE, so persisting them removes hundreds of
/// public-ESI lookups from warm starts.
/// </summary>
public sealed class NameCacheSnapshot
{
    public Dictionary<int, string> Systems { get; set; } = [];
    public Dictionary<int, string> Types { get; set; } = [];
    public Dictionary<long, string[]> Corps { get; set; } = []; // [name, ticker]

    public static NameCacheSnapshot? LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<NameCacheSnapshot>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null; // corrupt cache — rebuild from live ESI
        }
    }

    public void SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // best-effort cache — never fail the app over it
        }
    }
}
