namespace K162.Core;

/// <summary>Threat level derived from recent kill activity in a system.</summary>
public enum ThreatLevel { Calm, Warm, Hot }

/// <summary>Intel lookback window used for the "who lives here" analysis.</summary>
public enum Lookback { FortyEightHours, TwoWeeks, ThreeMonths }

public static class LookbackExtensions
{
    public static TimeSpan ToTimeSpan(this Lookback l) => l switch
    {
        Lookback.FortyEightHours => TimeSpan.FromHours(48),
        Lookback.TwoWeeks => TimeSpan.FromDays(14),
        _ => TimeSpan.FromDays(90),
    };

    public static string ToLabel(this Lookback l) => l switch
    {
        Lookback.FortyEightHours => "48 HOURS",
        Lookback.TwoWeeks => "2 WEEKS",
        _ => "3 MONTHS",
    };
}

/// <summary>A character authorization persisted between runs (refresh token is DPAPI-protected on disk).</summary>
public sealed record CharacterAuth(long CharacterId, string CharacterName, string RefreshToken);

/// <summary>Live per-pilot state maintained by the fleet tracker.</summary>
public sealed class PilotSnapshot
{
    public long CharacterId { get; init; }
    public string Name { get; set; } = "";
    public long CorporationId { get; set; }
    public string CorporationName { get; set; } = "";
    public string CorporationTicker { get; set; } = "";
    public string ShipName { get; set; } = "";
    public int SolarSystemId { get; set; }
    public string SystemName { get; set; } = "";
    public bool Online { get; set; }
    /// <summary>Last systems visited, oldest first, current system last. Max 4.</summary>
    public IReadOnlyList<string> Trail { get; set; } = [];
}

/// <summary>A static wormhole connection out of a J-space system.</summary>
public sealed record StaticConnection(string Code, string DestinationClass, long TotalMassKg)
{
    /// <summary>"2.0G" style label; 0 mass renders as "VAR" (variable / unknown).</summary>
    public string MassLabel => TotalMassKg switch
    {
        <= 0 => "VAR",
        >= 1_000_000_000 => (TotalMassKg / 1_000_000_000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "G",
        _ => (TotalMassKg / 1_000_000.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "M",
    };
}

/// <summary>One kill shown in the "recent kills in system" panel.</summary>
public sealed record KillRecord(
    long KillmailId,
    DateTimeOffset Time,
    string VictimShip,
    long VictimCorpId,
    string VictimCorpName,
    int AttackerCount);

/// <summary>A corporation ranked as a likely resident of a system.</summary>
public sealed record ResidentCorp(
    long CorpId,
    string Name,
    string Ticker,
    int PilotsSeen,
    int Kills,
    string ActiveTimezone,
    DateTimeOffset LastSeen);

/// <summary>Aggregated intel for one solar system.</summary>
public sealed class SystemIntel
{
    public int SolarSystemId { get; init; }
    public string SystemName { get; init; } = "";
    /// <summary>Display class, e.g. "C4", "C13 SHATTERED", "THERA"; empty for k-space.</summary>
    public string SystemClass { get; init; } = "";
    /// <summary>Wormhole effect name ("Wolf-Rayet", "Pulsar", ...) or empty.</summary>
    public string Effect { get; init; } = "";
    public IReadOnlyList<StaticConnection> Statics { get; init; } = [];
    /// <summary>Kills per 2h bin over the last 48h; index 0 oldest, 23 newest.</summary>
    public int[] KillBins { get; init; } = new int[24];
    public IReadOnlyList<KillRecord> RecentKills { get; init; } = [];
    public IReadOnlyList<ResidentCorp> Residents { get; init; } = [];
    /// <summary>Total killmails examined within the lookback window.</summary>
    public int KillsAnalyzed { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A recently-vacated system being watched for kills.</summary>
public sealed class HeldSystem
{
    public int SolarSystemId { get; init; }
    public string SystemName { get; init; } = "";
    public string PilotName { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Alerted { get; set; }
}

/// <summary>Detail of one killmail, decoupled from ESI JSON for aggregation and tests.</summary>
public sealed record KillmailDetail(
    long KillmailId,
    DateTimeOffset Time,
    int SolarSystemId,
    int VictimShipTypeId,
    long VictimCorpId,
    IReadOnlyList<long> AttackerCorpIds,
    IReadOnlyList<long> AttackerCharacterIds,
    int AttackerCount);
