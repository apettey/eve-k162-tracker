using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K162.App.Services;
using K162.Core;
using K162.Core.ChatLogs;
using K162.Core.Intel;
using K162.Core.Sso;
using K162.Core.Zkill;

namespace K162.App.ViewModels;

/// <summary>Top-level application state: pilots, view mode, wake watch, toast, status bar.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private readonly WakeWatch _wake = new();
    private readonly DispatcherTimer _secondTimer;
    private readonly CancellationTokenSource _appCts = new();
    private CancellationTokenSource? _toastCts;
    private Demo.DemoFleet? _demo;
    private bool _redisqStarted;
    private CancellationTokenSource? _chatLogCts;

    public MainViewModel(AppServices services)
    {
        _svc = services;
        Settings = _svc.Settings;
        _svc.Sound.SoundEnabled = () => Settings.SoundEnabled;

        Pilots.CollectionChanged += (_, _) => OnViewStateChanged();
        _wake.Changed += () => OnUi(RefreshHeldChips);

        _svc.Tracker.PilotChanged += s => OnUi(() => OnPilotChanged(s));
        _svc.Tracker.PilotJumped += j => OnUi(() => OnPilotJumped(j));
        _svc.Tracker.StatusChanged += st => OnUi(() => EsiStatusText = st switch
        {
            Core.EsiStatus.Connected => "CONNECTED",
            Core.EsiStatus.AuthRequired => "AUTH REQUIRED",
            Core.EsiStatus.Error => "ERROR",
            _ => "IDLE",
        });
        _svc.RedisQ.KillReceived += k => OnUi(() => OnLiveKill(k));
        _svc.RedisQ.ListeningChanged += on => OnUi(() => RedisqStatusText = on ? "LISTENING" : "RECONNECTING");
        _svc.Updates.Changed += () => OnUi(RefreshUpdateChip);

        _secondTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secondTimer.Tick += (_, _) => Tick();
        _secondTimer.Start();
        Tick();
    }

    public AppSettings Settings { get; }
    public ObservableCollection<PilotViewModel> Pilots { get; } = [];
    public ObservableCollection<HeldChipVm> HeldChips { get; } = [];

    [ObservableProperty] private PilotViewModel? _focusedPilot;
    [ObservableProperty] private ToastVm? _toast;
    [ObservableProperty] private bool _isDemo;
    [ObservableProperty] private string _windowTitle = "K162 Fleet Intel";
    [ObservableProperty] private string _onlineCountLabel = "0 PILOTS ONLINE";
    [ObservableProperty] private string _primaryActionLabel = "SETTINGS";
    [ObservableProperty] private string _esiStatusText = "IDLE";
    [ObservableProperty] private string _redisqStatusText = "OFF";
    [ObservableProperty] private string _chatLogStatusText = "OFF";
    [ObservableProperty] private string _tqTime = "";
    [ObservableProperty] private string _holdHelperText = "";
    [ObservableProperty] private string _lookbackLabel = "2 WEEKS";
    [ObservableProperty] private string _versionText = "";
    [ObservableProperty] private string _updateChipText = "";
    [ObservableProperty] private string _setupStatus = "";

    public bool IsSetup => Pilots.Count == 0;
    public bool IsGrid => Pilots.Count > 0 && FocusedPilot is null;
    public bool IsFocus => FocusedPilot is not null;
    public bool HasHeld => HeldChips.Count > 0;
    public bool HasToast => Toast is not null;
    public bool HasUpdate => UpdateChipText.Length > 0;

    /// <summary>Raised when a wake-kill alert should also flash the window / play attention cues.</summary>
    public event Action? AlertRaised;
    /// <summary>Raised when the SETTINGS action is invoked (the view opens the dialog).</summary>
    public event Action? SettingsRequested;

    partial void OnFocusedPilotChanged(PilotViewModel? value)
    {
        foreach (var p in Pilots) p.IsSelected = p == value;
        OnViewStateChanged();
    }

    partial void OnToastChanged(ToastVm? value) => OnPropertyChanged(nameof(HasToast));
    partial void OnUpdateChipTextChanged(string value) => OnPropertyChanged(nameof(HasUpdate));

    private void OnViewStateChanged()
    {
        OnPropertyChanged(nameof(IsSetup));
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(IsFocus));
        RefreshCounts();
    }

    // ---- startup ----

    public void Start()
    {
        VersionText = $"v{_svc.Updates.CurrentVersion} · .NET 10 · WPF · win-x64";
        LookbackLabel = Settings.Lookback.ToLabel();
        HoldHelperText = $"kills in systems you left < {Settings.ClampedHoldMinutes} min ago trigger an alert";
        _ = _svc.Updates.RunAsync(_appCts.Token);

        var saved = _svc.Tokens.Load();
        if (saved.Count > 0 && Settings.EsiClientId.Length > 0)
        {
            _svc.Tracker.Configure(Settings.EsiClientId);
            foreach (var auth in saved) TrackAuth(auth);
        }
    }

    private void TrackAuth(CharacterAuth auth)
    {
        if (Pilots.All(p => p.CharacterId != auth.CharacterId))
        {
            Pilots.Add(new PilotViewModel { CharacterId = auth.CharacterId, Name = auth.CharacterName, PortraitUrl = ImageUrls.Portrait(auth.CharacterId) });
        }
        _svc.Tracker.Track(auth, _appCts.Token);
        PrimaryActionLabel = "SETTINGS";
        EnsureRedisQ();
        RestartChatLogWatcher();
        OnViewStateChanged();
    }

    /// <summary>(Re)starts the Local chat-log tail — the fast path for system changes.</summary>
    private void RestartChatLogWatcher()
    {
        _chatLogCts?.Cancel();
        _chatLogCts = null;
        if (IsDemo || Pilots.Count == 0 || !Settings.ChatLogsEnabled)
        {
            ChatLogStatusText = "OFF";
            return;
        }
        var cts = _chatLogCts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
        var watcher = new ChatLogWatcher(Settings.ChatLogDirectory);
        watcher.WatchingChanged += n => OnUi(() =>
        {
            if (!cts.IsCancellationRequested)
                ChatLogStatusText = n > 0 ? $"WATCHING {n}" : "NO LOGS";
        });
        watcher.SystemChanged += change =>
            _ = _svc.Tracker.ReportLocalSystemAsync(change.CharacterId, change.ListenerName, change.SystemName, cts.Token);
        ChatLogStatusText = "SCANNING";
        _ = watcher.RunAsync(cts.Token);
    }

    private void EnsureRedisQ()
    {
        if (_redisqStarted || IsDemo) return;
        _redisqStarted = true;
        _ = _svc.RedisQ.RunAsync(_appCts.Token);
    }

    // ---- commands ----

    [RelayCommand]
    private void FocusPilot(PilotViewModel pilot) => FocusedPilot = pilot;

    [RelayCommand]
    private void BackToGrid() => FocusedPilot = null;

    [RelayCommand]
    private void PrimaryAction()
    {
        if (IsDemo) _demo?.SimulateJump();
        else SettingsRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void DismissToast()
    {
        _toastCts?.Cancel();
        Toast = null;
    }

    [RelayCommand]
    private void StartDemo()
    {
        if (IsDemo) return;
        IsDemo = true;
        PrimaryActionLabel = "SIMULATE JUMP";
        EsiStatusText = "DEMO";
        RedisqStatusText = "DEMO";
        WindowTitle = "K162 Fleet Intel — Cold Static [CSTAT]";
        _demo = new Demo.DemoFleet(this, _svc.Wormholes);
        _demo.Start();
    }

    [RelayCommand]
    private async Task AddCharacterAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.EsiClientId))
        {
            SetupStatus = "Enter your EVE SSO client id first (developers.eveonline.com).";
            return;
        }
        _svc.SettingsStore.Save(Settings);
        try
        {
            SetupStatus = "Waiting for EVE SSO in your browser…";
            var tokens = await _svc.Sso.LoginAsync(Settings.EsiClientId, Settings.CallbackPort, _appCts.Token);
            var auth = new CharacterAuth(tokens.CharacterId, tokens.CharacterName, tokens.RefreshToken);
            _svc.Tokens.Upsert(auth);
            _svc.Tracker.Configure(Settings.EsiClientId);
            SetupStatus = "";
            TrackAuth(auth);
        }
        catch (Exception ex)
        {
            SetupStatus = "Login failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveCharacter(PilotViewModel pilot)
    {
        _svc.Tokens.Remove(pilot.CharacterId);
        _svc.Tracker.Untrack(pilot.CharacterId);
        if (FocusedPilot == pilot) FocusedPilot = null;
        Pilots.Remove(pilot);
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync() => await _svc.Updates.ApplyAsync();

    [RelayCommand]
    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no default browser — ignore */ }
    }

    // ---- settings changed from the dialog ----

    public void OnSettingsSaved()
    {
        _svc.SettingsStore.Save(Settings);
        HoldHelperText = $"kills in systems you left < {Settings.ClampedHoldMinutes} min ago trigger an alert";
        if (Settings.Lookback.ToLabel() != LookbackLabel)
        {
            LookbackLabel = Settings.Lookback.ToLabel();
            _svc.Intel.ClearCache();
            foreach (var p in Pilots.Where(p => p.SolarSystemId > 0 && !IsDemo))
                _ = RefreshIntelAsync(p);
        }
        _svc.Tracker.Configure(Settings.EsiClientId);
        RestartChatLogWatcher();
    }

    // ---- tracker events (UI thread) ----

    private void OnPilotChanged(PilotSnapshot s)
    {
        var vm = Pilots.FirstOrDefault(p => p.CharacterId == s.CharacterId);
        if (vm is null) return;
        var hadSystem = vm.SolarSystemId;
        vm.ApplySnapshot(s);
        RefreshCounts();
        RefreshWindowTitle(s);
        if (s.SolarSystemId > 0 && hadSystem != s.SolarSystemId)
            _ = RefreshIntelAsync(vm);
    }

    private void OnPilotJumped(JumpEvent j)
    {
        var vm = Pilots.FirstOrDefault(p => p.CharacterId == j.Pilot.CharacterId);
        vm?.Flash();
        _svc.Sound.Ping();
        _wake.OnSystemLeft(j.FromSystemId, j.FromSystemName, j.Pilot.Name,
            TimeSpan.FromMinutes(Settings.ClampedHoldMinutes), DateTimeOffset.UtcNow);
        _wake.OnSystemEntered(j.ToSystemId);
    }

    private async Task RefreshIntelAsync(PilotViewModel vm)
    {
        try
        {
            var intel = await _svc.Intel.GetIntelAsync(vm.SolarSystemId, Settings.Lookback, _appCts.Token);
            OnUi(() => { if (vm.SolarSystemId == intel.SolarSystemId) vm.ApplyIntel(intel, LookbackLabel); });
        }
        catch (Exception) { /* transient API failure — next refresh will retry */ }
    }

    private void RefreshWindowTitle(PilotSnapshot s)
    {
        if (IsDemo || string.IsNullOrEmpty(s.CorporationName)) return;
        WindowTitle = $"K162 Fleet Intel — {s.CorporationName} [{s.CorporationTicker}]";
    }

    private void RefreshCounts() =>
        OnlineCountLabel = $"{Pilots.Count(p => p.Online)} PILOTS ONLINE";

    // ---- live kills / wake alerts ----

    private async void OnLiveKill(LiveKill kill)
    {
        // A system is "subscribed" while a pilot is in it, or for the Wake Watch hold
        // window after the last pilot left. Everything else in the RedisQ firehose is dropped.
        var isHeld = _wake.Held.Any(h => h.SolarSystemId == kill.SolarSystemId);
        var isOccupied = Pilots.Any(p => p.SolarSystemId == kill.SolarSystemId);
        if (!isHeld && !isOccupied) return;

        var shipName = await _svc.Esi.GetTypeNameAsync(kill.VictimShipTypeId, _appCts.Token) ?? "Ship";

        if (isHeld && _wake.TryAlert(kill.SolarSystemId) is { } held)
        {
            RaiseWakeAlert(held.SystemName,
                $"{shipName} destroyed · {kill.AttackerCount} attackers",
                $"{held.PilotName}'s wake",
                $"https://zkillboard.com/kill/{kill.KillmailId}/");
        }

        // Fold the kill into the per-system intel cache (occupied AND recently-vacated
        // systems), so cards update live and a re-entry within the cache TTL is fresh.
        var corp = kill.VictimCorpId > 0 ? await _svc.Esi.GetCorporationAsync(kill.VictimCorpId, _appCts.Token) : null;
        var updated = _svc.Intel.ApplyLiveKill(kill, shipName, corp?.Name ?? "");
        if (updated is not null)
            OnUi(() =>
            {
                foreach (var p in Pilots.Where(p => p.SolarSystemId == kill.SolarSystemId))
                    p.ApplyIntel(updated, LookbackLabel);
            });
    }

    /// <summary>Shared alert path for live and demo wake kills: toast + red chip + double ping.</summary>
    public void RaiseWakeAlert(string systemName, string detail, string who, string url)
    {
        _svc.Sound.DoublePing();
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        Toast = new ToastVm(systemName, detail, who, url);
        AlertRaised?.Invoke();
        _ = DismissToastLaterAsync(cts.Token);
    }

    private async Task DismissToastLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(9), ct);
            OnUi(() => Toast = null);
        }
        catch (OperationCanceledException) { }
    }

    // ---- demo hooks ----

    public WakeWatch Wake => _wake;
    public SoundService Sound => _svc.Sound;

    public void HoldSystem(int systemId, string systemName, string pilotName) =>
        _wake.OnSystemLeft(systemId, systemName, pilotName,
            TimeSpan.FromMinutes(Settings.ClampedHoldMinutes), DateTimeOffset.UtcNow);

    // ---- per-second tick ----

    private void Tick()
    {
        TqTime = "TQ TIME " + DateTime.UtcNow.ToString("HH:mm");
        _wake.Prune(DateTimeOffset.UtcNow);
        foreach (var chip in HeldChips)
        {
            var held = _wake.Held.FirstOrDefault(h => h.SolarSystemId == chip.SystemId);
            if (held is null) continue;
            chip.Remaining = FormatRemaining(held.ExpiresAt);
            chip.Alerted = held.Alerted;
        }
    }

    private void RefreshHeldChips()
    {
        var now = DateTimeOffset.UtcNow;
        HeldChips.Clear();
        foreach (var h in _wake.Held)
        {
            HeldChips.Add(new HeldChipVm
            {
                SystemId = h.SolarSystemId,
                SystemName = h.SystemName,
                PilotFirstName = h.PilotName.Split(' ')[0],
                Remaining = FormatRemaining(h.ExpiresAt),
                Alerted = h.Alerted,
            });
        }
        OnPropertyChanged(nameof(HasHeld));
    }

    private static string FormatRemaining(DateTimeOffset expiresAt)
    {
        var s = Math.Max(0, (int)Math.Round((expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private void RefreshUpdateChip()
    {
        UpdateChipText = _svc.Updates.Status switch
        {
            "available" => $"UPDATE v{_svc.Updates.AvailableVersion} ↗",
            "downloading" => "UPDATING…",
            "failed" => "UPDATE FAILED",
            _ => "",
        };
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Shutdown() => _appCts.Cancel();
}

/// <summary>Bundle of long-lived services composed in App.xaml.cs.</summary>
public sealed record AppServices(
    AppSettings Settings,
    SettingsService SettingsStore,
    TokenStore Tokens,
    SsoService Sso,
    Core.Esi.EsiClient Esi,
    ZkillClient Zkill,
    RedisQListener RedisQ,
    WormholeDb Wormholes,
    IntelService Intel,
    FleetTracker Tracker,
    SoundService Sound,
    UpdateService Updates);
