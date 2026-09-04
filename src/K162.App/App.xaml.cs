using System.IO;
using System.Net.Http;
using System.Windows;
using K162.App.Services;
using K162.App.ViewModels;
using K162.Core;
using K162.Core.Esi;
using K162.Core.Intel;
using K162.Core.Sso;
using K162.Core.Zkill;

namespace K162.App;

public partial class App : Application
{
    private MainViewModel? _vm;

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
        var esi = new EsiClient(http);
        var zkill = new ZkillClient(http);
        var sso = new SsoService(http);
        var tokens = new TokenStore();

        _vm = new MainViewModel(new AppServices(
            settings, settingsStore, tokens, sso, esi, zkill,
            new RedisQListener(http), wormholes,
            new IntelService(esi, zkill, wormholes),
            new FleetTracker(esi, sso, tokens, wormholes),
            new SoundService(), new UpdateService()));

        var window = new MainWindow(_vm);
        MainWindow = window;
        window.Show();
        _vm.Start();
        if (e.Args.Contains("--demo"))
            _vm.StartDemoCommand.Execute(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Shutdown();
        base.OnExit(e);
    }
}
