using CommunityToolkit.Mvvm.ComponentModel;
using K162.Core;

namespace K162.App.ViewModels;

/// <summary>A static-connection chip ("X877 → C4", focus view adds mass "2.0G").</summary>
public sealed record StaticChipVm(string Code, string Destination, string MassLabel)
{
    public string CardText => $"{Code} → {Destination}";
    public static StaticChipVm From(StaticConnection s) => new(s.Code, s.DestinationClass, s.MassLabel);
}

/// <summary>One row in the "recent kills in system" panel.</summary>
public sealed record KillRowVm(string TimeText, string CorpLogoUrl, string Ship, string VictimCorp, string InvolvedText, string Url)
{
    public static KillRowVm From(KillRecord k, string systemName) => new(
        k.Time.UtcDateTime.ToString("HH:mm"),
        ImageUrls.CorpLogo(k.VictimCorpId, 64),
        k.VictimShip,
        string.IsNullOrEmpty(k.VictimCorpName) ? "Independent" : k.VictimCorpName,
        $"{k.AttackerCount}×",
        $"https://zkillboard.com/kill/{k.KillmailId}/");
}

/// <summary>One row in the "who lives here" panel.</summary>
public sealed record ResidentRowVm(
    string LogoUrl, string Name, string TickerText,
    string PilotsText, string KillsText, string TzText, string LastSeenText, bool IsNow, string Url)
{
    public static ResidentRowVm From(ResidentCorp r, DateTimeOffset now)
    {
        var lastSeen = Core.Intel.IntelAggregator.FormatLastSeen(r.LastSeen, now);
        return new ResidentRowVm(
            ImageUrls.CorpLogo(r.CorpId, 64),
            r.Name,
            $"[{r.Ticker}]",
            $"{r.PilotsSeen} pilots seen",
            $"{r.Kills} kills",
            $"active {r.ActiveTimezone}",
            $"last {lastSeen}",
            lastSeen == "now",
            $"https://zkillboard.com/corporation/{r.CorpId}/");
    }
}

/// <summary>One breadcrumb segment of the trail row.</summary>
public sealed record TrailVm(string Text, bool IsCurrent);

/// <summary>A Wake Watch chip: "J130930 · Vex · 6:23", red + pulsing once alerted.</summary>
public sealed partial class HeldChipVm : ObservableObject
{
    public int SystemId { get; init; }
    public string SystemName { get; init; } = "";
    public string PilotFirstName { get; init; } = "";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private bool _alerted;
}

/// <summary>The kill-in-wake toast content.</summary>
public sealed record ToastVm(string SystemName, string Detail, string Who, string Url);

public static class ImageUrls
{
    public static string Portrait(long characterId, int size = 64) =>
        $"https://images.evetech.net/characters/{characterId}/portrait?size={size}";

    public static string CorpLogo(long corpId, int size = 64) =>
        corpId > 0 ? $"https://images.evetech.net/corporations/{corpId}/logo?size={size}"
                   : $"https://images.evetech.net/corporations/1000001/logo?size={size}";
}
