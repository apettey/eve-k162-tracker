using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using K162.App.Services;
using K162.App.ViewModels;
using K162.Core;
using K162.Core.Caching;
using K162.Core.Esi;
using K162.Core.Intel;
using K162.Core.Sso;
using K162.Core.Zkill;

namespace K162.App;

public partial class App : Application
{
    private MainViewModel? _vm;
    private EsiClient? _esi;
    private string? _nameCachePath;
    private int _savedNameVersion;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack hooks must run before anything else: they handle the
        // install/update/uninstall lifecycle events and may exit the process.
        Velopack.VelopackApp.Build().Run();
        base.OnStartup(e);

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"K162FleetIntel/{version}");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/apettey/eve-k162-tracker)");

        var settingsStore = new SettingsService();
        var settings = settingsStore.Load();
        var wormholes = WormholeDb.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "wormholes.json"));
        var esi = _esi = new EsiClient(http);
        var zkill = new ZkillClient(http);
        var sso = new SsoService(http);
        var tokens = new TokenStore();

        // Disk caches under %LOCALAPPDATA%: killmails are immutable (permanent, pruned by
        // age), names near-immutable, intel snapshots make warm starts paint instantly.
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "K162FleetIntel", "cache");
        _nameCachePath = Path.Combine(cacheDir, "names.json");
        if (NameCacheSnapshot.LoadFrom(_nameCachePath) is { } names)
            esi.ImportNameCache(names);
        _savedNameVersion = esi.NameCacheVersion;
        var killmails = new KillmailStore(Path.Combine(cacheDir, "killmails.jsonl"));

        _vm = new MainViewModel(new AppServices(
            settings, settingsStore, tokens, sso, esi, zkill,
            new R2Z2Listener(http), wormholes,
            new IntelService(esi, zkill, wormholes, killmails, Path.Combine(cacheDir, "intel.json")),
            new FleetTracker(esi, sso, tokens, wormholes),
            new SoundService(), new UpdateService()));

        // Persist the name caches every 5 minutes when they grew, and on exit.
        var nameCacheTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        nameCacheTimer.Tick += (_, _) => SaveNameCache();
        nameCacheTimer.Start();

        var window = new MainWindow(_vm);
        MainWindow = window;
        window.Show();
        _vm.Start();
        if (e.Args.Contains("--demo"))
            _vm.StartDemoCommand.Execute(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveNameCache();
        _vm?.Shutdown();
        base.OnExit(e);
    }

    private void SaveNameCache()
    {
        if (_esi is null || _nameCachePath is null) return;
        var version = _esi.NameCacheVersion;
        if (version == _savedNameVersion) return;
        _savedNameVersion = version;
        _esi.ExportNameCache().SaveTo(_nameCachePath);
    }
}
