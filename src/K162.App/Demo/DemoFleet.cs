using K162.App.ViewModels;
using K162.Core;
using K162.Core.Intel;

namespace K162.App.Demo;

/// <summary>
/// Demo mode: the design prototype's four pilots and simulated jump / wake-kill loop,
/// wired through the same view-model paths the live tracker uses. Wormhole class,
/// effect and statics come from the real bundled database.
/// </summary>
public sealed class DemoFleet(MainViewModel main, WormholeDb wormholes)
{
    private static readonly string[] JumpPool = ["J115844", "J102053", "J151520", "J154854", "J133013"];
    private static readonly string[] VictimShips = ["Venture", "Heron", "Epithal", "Astero", "Drake"];
    private readonly Random _rng = new();
    private CancellationTokenSource? _killCts;

    public void Start()
    {
        foreach (var p in BuildPilots()) main.Pilots.Add(p);
        // Two systems already held, as in the prototype.
        HoldFor("J130930", "Vex Arkanis", TimeSpan.FromMinutes(6.5));
        HoldFor("J160941", "Dren Kovacs", TimeSpan.FromMinutes(2));
    }

    private void HoldFor(string systemName, string pilot, TimeSpan hold)
    {
        var id = wormholes.TryGetId(systemName, out var real) ? real : -Math.Abs(systemName.GetHashCode());
        main.Wake.OnSystemLeft(id, systemName, pilot, hold, DateTimeOffset.UtcNow);
    }

    public void SimulateJump()
    {
        if (main.Pilots.Count == 0) return;
        var pilot = main.Pilots[_rng.Next(main.Pilots.Count)];
        var newSystem = JumpPool[_rng.Next(JumpPool.Length)];

        // Old system enters the wake watch.
        HoldFor(pilot.SystemName, pilot.Name, TimeSpan.FromMinutes(main.Settings.ClampedHoldMinutes));

        MoveTo(pilot, newSystem, RandomBins());
        pilot.Flash();
        main.Sound.Ping();

        // A kill lands in someone's wake a few seconds later.
        _killCts?.Cancel();
        var cts = _killCts = new CancellationTokenSource();
        _ = WakeKillLaterAsync(cts.Token);
    }

    private async Task WakeKillLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(4 + _rng.NextDouble() * 4), ct); }
        catch (OperationCanceledException) { return; }
        var held = main.Wake.Held;
        if (held.Count == 0) return;
        var h = held[_rng.Next(held.Count)];
        main.Wake.TryAlert(h.SolarSystemId);
        main.RaiseWakeAlert(h.SystemName,
            $"{VictimShips[_rng.Next(VictimShips.Length)]} destroyed · {2 + _rng.Next(8)} attackers",
            $"{h.PilotName}'s wake",
            $"https://zkillboard.com/system/{h.SystemName}/");
    }

    private void MoveTo(PilotViewModel pilot, string systemName, int[] bins)
    {
        var wh = wormholes.FindByName(systemName);
        pilot.SystemName = systemName;
        pilot.SystemClass = wh is null ? "C" + (2 + _rng.Next(4)) : WormholeDb.FormatClass(wh.RawClass);
        pilot.Effect = wh?.Effect ?? "";
        pilot.Statics.Clear();
        foreach (var s in wh?.Statics ?? []) pilot.Statics.Add(StaticChipVm.From(s));
        pilot.Bins = bins;
        pilot.SetThreat(IntelAggregator.ComputeThreat(bins));
        pilot.KillSummary = $"{bins.Sum()} KILLS / 48H";
        pilot.ZkillUrl = $"https://zkillboard.com/system/{systemName}/";
        pilot.AnoikisUrl = $"https://anoik.is/systems/{systemName}";
        var trail = pilot.Trail.Select(t => t.Text.Replace("› ", "")).ToList();
        trail.Add(systemName);
        pilot.SetTrail(trail.TakeLast(4).ToList());
    }

    private int[] RandomBins() =>
        [.. Enumerable.Range(0, 24).Select(_ => _rng.NextDouble() < 0.75 ? 0 : 1 + _rng.Next(5))];

    private List<PilotViewModel> BuildPilots()
    {
        var vex = MakePilot(1, "Vex Arkanis", "Loki", 90000101, "J142822",
            [0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0], 214,
            [("11:42", "Epithal", "Sable Industries", 3), ("03:15", "Venture", "Krait Mining Co", 2)],
            [("Hole Punchers", "HPNCH", 12, 41, "EU", "2h ago", false),
             ("Sable Industries", "SABLE", 5, 8, "US", "11h ago", false)],
            ["J130930", "J144208", "J142822"]);

        var mira = MakePilot(2, "Mira Solano", "Astero", 90000102, "J112747",
            [0, 1, 0, 0, 2, 1, 0, 0, 0, 3, 1, 0, 0, 0, 1, 0, 2, 0, 0, 1, 0, 0, 2, 1], 388,
            [("13:07", "Astero", "Nomad Salvage", 1), ("09:51", "Heron", "Independent", 4), ("22:30", "Gnosis", "Redshift Cartel", 6)],
            [("Redshift Cartel", "RDSH", 19, 112, "US/AU", "40m ago", false),
             ("Void Tenants", "VOIDT", 7, 23, "EU", "6h ago", false),
             ("Nomad Salvage", "NOMS", 3, 2, "US", "3d ago", false)],
            ["J142822", "J112747"]);

        var dren = MakePilot(3, "Dren Kovacs", "Buzzard", 90000103, "Thera",
            [1, 2, 0, 3, 1, 2, 4, 1, 0, 2, 3, 1, 2, 5, 2, 1, 3, 2, 4, 2, 3, 5, 4, 6], 1204,
            [("14:58", "Sabre", "Gate Watch", 11), ("14:32", "Pacifier", "Signal Cartel", 8),
             ("13:44", "Hecate", "Wanderers Inc", 9), ("12:05", "Astero", "Independent", 5)],
            [("Signal Cartel", "1SIG", 88, 14, "ALL", "now", true),
             ("Gate Watch", "GWTCH", 22, 203, "EU/US", "now", true),
             ("Thera Squatters", "THSQ", 9, 67, "RU", "1h ago", false)],
            ["J105433", "J160941", "Thera"]);

        var ilya = MakePilot(4, "Ilya Von Sarum", "Praxis", 90000104, "J130026",
            [0, 0, 0, 1, 0, 0, 0, 0, 2, 1, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 1, 0, 0, 0], 156,
            [("08:12", "Drake", "Krab Collective", 7), ("01:47", "Praxis", "Krab Collective", 7)],
            [("Krab Collective", "KRAB", 15, 19, "AU", "5h ago", false),
             ("Fifth Circle", "5CRC", 11, 88, "EU", "9h ago", false)],
            ["J142822", "J130026"]);

        return [vex, mira, dren, ilya];
    }

    private PilotViewModel MakePilot(
        long id, string name, string ship, long portraitId, string systemName,
        int[] bins, int killsAnalyzed,
        (string Time, string Ship, string Corp, int Involved)[] kills,
        (string Name, string Ticker, int Pilots, int Kills, string Tz, string LastSeen, bool IsNow)[] residents,
        string[] trail)
    {
        var vm = new PilotViewModel
        {
            CharacterId = id,
            Name = name,
            ShipName = ship,
            Online = true,
            PortraitUrl = ImageUrls.Portrait(portraitId),
            CorpLogoUrl = ImageUrls.CorpLogo(98000001),
            CorpDisplay = "Cold Static [CSTAT]",
        };
        MoveTo(vm, systemName, bins);
        vm.SetTrail(trail);
        vm.KillsAnalyzedLabel = $"Derived from {killsAnalyzed} killmails over {main.Settings.Lookback.ToLabel()}";
        vm.ResidentSummary = $"{residents.Length} RESIDENT GROUPS";
        vm.KillSummary = $"{kills.Length} KILLS / 48H";
        vm.Kills.Clear();
        foreach (var k in kills)
            vm.Kills.Add(new KillRowVm(k.Time, ImageUrls.CorpLogo(1000001), k.Ship, k.Corp, $"{k.Involved}×", vm.ZkillUrl));
        vm.Residents.Clear();
        foreach (var r in residents)
            vm.Residents.Add(new ResidentRowVm(
                ImageUrls.CorpLogo(1000001), r.Name, $"[{r.Ticker}]",
                $"{r.Pilots} pilots seen", $"{r.Kills} kills", $"active {r.Tz}", $"last {r.LastSeen}", r.IsNow,
                "https://zkillboard.com/"));
        return vm;
    }
}
