using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using K162.Core;
using K162.Core.Intel;

namespace K162.App.ViewModels;

/// <summary>Everything one pilot card / focus view binds to.</summary>
public partial class PilotViewModel : ObservableObject
{
    public long CharacterId { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _shipName = "";
    [ObservableProperty] private string _portraitUrl = "";
    [ObservableProperty] private string _corpLogoUrl = "";
    [ObservableProperty] private string _corpDisplay = "";
    [ObservableProperty] private bool _online;

    [ObservableProperty] private string _systemName = "—";
    [ObservableProperty] private string _systemClass = "";
    [ObservableProperty] private string _effect = "";
    [ObservableProperty] private int[] _bins = new int[24];
    [ObservableProperty] private ThreatLevel _threat = ThreatLevel.Calm;
    [ObservableProperty] private string _threatLabel = "CALM";
    [ObservableProperty] private string _focusThreatLabel = "THREAT: CALM";
    [ObservableProperty] private string _killSummary = "0 KILLS / 48H";
    [ObservableProperty] private string _residentSummary = "0 RESIDENT GROUPS";
    [ObservableProperty] private string _killsAnalyzedLabel = "";
    [ObservableProperty] private string _zkillUrl = "https://zkillboard.com/";
    [ObservableProperty] private string _anoikisUrl = "https://anoik.is/";
    [ObservableProperty] private bool _isFlashing;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<StaticChipVm> Statics { get; } = [];
    public ObservableCollection<KillRowVm> Kills { get; } = [];
    public ObservableCollection<ResidentRowVm> Residents { get; } = [];
    public ObservableCollection<TrailVm> Trail { get; } = [];

    public int SolarSystemId { get; set; }

    public string ShipCorpLine => $"{CorpDisplay} · {ShipName}";
    public string RailLine => $"{SystemName} · {ShipName}";

    partial void OnCorpDisplayChanged(string value) => OnPropertyChanged(nameof(ShipCorpLine));
    partial void OnShipNameChanged(string value) { OnPropertyChanged(nameof(ShipCorpLine)); OnPropertyChanged(nameof(RailLine)); }
    partial void OnSystemNameChanged(string value) => OnPropertyChanged(nameof(RailLine));

    public void ApplySnapshot(PilotSnapshot s)
    {
        Name = s.Name;
        ShipName = s.ShipName;
        Online = s.Online;
        PortraitUrl = ImageUrls.Portrait(s.CharacterId);
        if (s.CorporationId > 0)
        {
            CorpLogoUrl = ImageUrls.CorpLogo(s.CorporationId);
            CorpDisplay = string.IsNullOrEmpty(s.CorporationTicker)
                ? s.CorporationName : $"{s.CorporationName} [{s.CorporationTicker}]";
        }
        SolarSystemId = s.SolarSystemId;
        if (!string.IsNullOrEmpty(s.SystemName)) SystemName = s.SystemName;
        SetTrail(s.Trail);
    }

    public void SetTrail(IReadOnlyList<string> trail)
    {
        Trail.Clear();
        for (var i = 0; i < trail.Count; i++)
            Trail.Add(new TrailVm((i > 0 ? "› " : "") + trail[i], i == trail.Count - 1));
    }

    public void ApplyIntel(SystemIntel intel, string lookbackLabel)
    {
        SystemName = intel.SystemName;
        SystemClass = intel.SystemClass;
        Effect = intel.Effect;
        Bins = intel.KillBins;
        SetThreat(IntelAggregator.ComputeThreat(intel.KillBins));

        Statics.Clear();
        foreach (var s in intel.Statics) Statics.Add(StaticChipVm.From(s));

        Kills.Clear();
        foreach (var k in intel.RecentKills) Kills.Add(KillRowVm.From(k, intel.SystemName));

        var now = DateTimeOffset.UtcNow;
        Residents.Clear();
        foreach (var r in intel.Residents) Residents.Add(ResidentRowVm.From(r, now));

        KillSummary = $"{intel.KillBins.Sum()} KILLS / 48H";
        ResidentSummary = $"{intel.Residents.Count} RESIDENT GROUPS";
        KillsAnalyzedLabel = $"Derived from {intel.KillsAnalyzed} killmails over {lookbackLabel}";
        ZkillUrl = $"https://zkillboard.com/system/{intel.SystemName}/";
        AnoikisUrl = $"https://anoik.is/systems/{intel.SystemName}";
    }

    public void SetThreat(ThreatLevel level)
    {
        Threat = level;
        ThreatLabel = level.ToString().ToUpperInvariant();
        FocusThreatLabel = "THREAT: " + ThreatLabel;
    }

    /// <summary>Triggers the 1.5s jump-flash animation on the card.</summary>
    public async void Flash()
    {
        IsFlashing = false;
        IsFlashing = true;
        await Task.Delay(1600);
        IsFlashing = false;
    }
}
