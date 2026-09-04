using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace K162.Core.Sso;

/// <summary>
/// Persists per-character refresh tokens under %APPDATA%\K162FleetIntel, DPAPI-encrypted
/// for the current user (Windows). On non-Windows (tests/CI) it stores plaintext.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;

    public TokenStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K162FleetIntel");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "tokens.dat");
    }

    public IReadOnlyList<CharacterAuth> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var raw = File.ReadAllBytes(_path);
            if (OperatingSystem.IsWindows())
                raw = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<CharacterAuth>>(Encoding.UTF8.GetString(raw)) ?? [];
        }
        catch (Exception)
        {
            // Unreadable/corrupt store (e.g. different user profile) — treat as no saved logins.
            return [];
        }
    }

    public void Save(IEnumerable<CharacterAuth> auths)
    {
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(auths.ToList()));
        if (OperatingSystem.IsWindows())
            raw = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, raw);
    }

    public void Upsert(CharacterAuth auth)
    {
        var list = Load().Where(a => a.CharacterId != auth.CharacterId).ToList();
        list.Add(auth);
        Save(list);
    }

    public void Remove(long characterId) => Save(Load().Where(a => a.CharacterId != characterId));
}
