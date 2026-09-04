using System.Text.Json;
using System.Text.Json.Serialization;

namespace K162.Core;

public sealed class AppSettings
{
    public bool SoundEnabled { get; set; } = true;
    /// <summary>Wake Watch hold time in minutes (1–60).</summary>
    public int HoldMinutes { get; set; } = 10;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Lookback Lookback { get; set; } = Lookback.TwoWeeks;
    /// <summary>EVE SSO application client id (registered at developers.eveonline.com).</summary>
    public string EsiClientId { get; set; } = "";
    /// <summary>Localhost port the SSO callback listener binds to.</summary>
    public int CallbackPort { get; set; } = 8410;

    public int ClampedHoldMinutes => Math.Clamp(HoldMinutes, 1, 60);
}

/// <summary>Loads/saves settings JSON under %APPDATA%\K162FleetIntel.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K162FleetIntel");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt settings file — fall back to defaults rather than failing startup.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
}
