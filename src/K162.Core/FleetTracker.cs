using K162.Core.Esi;
using K162.Core.Sso;

namespace K162.Core;

public enum EsiStatus { Idle, Connected, AuthRequired, Error }

public sealed record JumpEvent(PilotSnapshot Pilot, int FromSystemId, string FromSystemName, int ToSystemId);

/// <summary>
/// Polls ESI for every authorized character (online → location every ~5s, ship every 30s),
/// maintains pilot snapshots and trails, and raises jump events.
/// </summary>
public sealed class FleetTracker(EsiClient esi, SsoService sso, TokenStore tokenStore, WormholeDb wormholes)
{
    private static readonly TimeSpan LocationInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<long, Tracked> _pilots = [];
    private readonly Lock _lock = new();
    private string _clientId = "";

    public event Action<PilotSnapshot>? PilotChanged;
    public event Action<JumpEvent>? PilotJumped;
    public event Action<EsiStatus>? StatusChanged;

    public IReadOnlyList<PilotSnapshot> Pilots
    {
        get { lock (_lock) return _pilots.Values.Select(t => t.Snapshot).ToList(); }
    }

    public void Configure(string clientId) => _clientId = clientId;

    /// <summary>Starts (or restarts) a polling loop for a character.</summary>
    public void Track(CharacterAuth auth, CancellationToken appCt)
    {
        lock (_lock)
        {
            if (_pilots.TryGetValue(auth.CharacterId, out var existing))
                existing.Cts.Cancel();
            var tracked = new Tracked
            {
                Auth = auth,
                Snapshot = new PilotSnapshot { CharacterId = auth.CharacterId, Name = auth.CharacterName },
                Cts = CancellationTokenSource.CreateLinkedTokenSource(appCt),
            };
            _pilots[auth.CharacterId] = tracked;
            _ = RunPilotLoopAsync(tracked, tracked.Cts.Token);
        }
    }

    public void Untrack(long characterId)
    {
        lock (_lock)
        {
            if (_pilots.Remove(characterId, out var tracked))
                tracked.Cts.Cancel();
        }
    }

    private async Task RunPilotLoopAsync(Tracked t, CancellationToken ct)
    {
        var nextSlow = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var token = await GetAccessTokenAsync(t, ct);
                var now = DateTimeOffset.UtcNow;

                if (now >= nextSlow)
                {
                    nextSlow = now + SlowInterval;
                    var online = await esi.GetOnlineAsync(t.Auth.CharacterId, token, ct);
                    if (online.HasValue && online.Value != t.Snapshot.Online)
                    {
                        t.Snapshot.Online = online.Value;
                        PilotChanged?.Invoke(t.Snapshot);
                    }
                    if (t.Snapshot.Online)
                    {
                        var ship = await esi.GetShipAsync(t.Auth.CharacterId, token, ct);
                        if (ship is not null && ship.ShipName != t.Snapshot.ShipName)
                        {
                            t.Snapshot.ShipName = ship.ShipName;
                            PilotChanged?.Invoke(t.Snapshot);
                        }
                        if (t.Snapshot.CorporationId == 0)
                            await LoadCorpAsync(t, ct);
                    }
                }

                if (t.Snapshot.Online)
                {
                    var systemId = await esi.GetLocationAsync(t.Auth.CharacterId, token, ct);
                    if (systemId.HasValue && systemId.Value != t.Snapshot.SolarSystemId)
                        await HandleJumpAsync(t, systemId.Value, ct);
                }
                StatusChanged?.Invoke(EsiStatus.Connected);
                await Task.Delay(t.Snapshot.Online ? LocationInterval : SlowInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (EsiUnauthorizedException)
            {
                t.AccessToken = null; // force refresh next round
                await SafeDelay(TimeSpan.FromSeconds(5), ct);
            }
            catch (Exception)
            {
                StatusChanged?.Invoke(EsiStatus.Error);
                await SafeDelay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }

    private async Task HandleJumpAsync(Tracked t, int newSystemId, CancellationToken ct)
    {
        var fromId = t.Snapshot.SolarSystemId;
        var fromName = t.Snapshot.SystemName;
        var name = wormholes.Find(newSystemId)?.Name
            ?? await esi.GetSystemNameAsync(newSystemId, ct) ?? $"System {newSystemId}";

        t.Snapshot.SolarSystemId = newSystemId;
        t.Snapshot.SystemName = name;
        t.Snapshot.Trail = [.. t.Snapshot.Trail.TakeLast(3), name];
        PilotChanged?.Invoke(t.Snapshot);
        if (fromId != 0)
            PilotJumped?.Invoke(new JumpEvent(t.Snapshot, fromId, fromName, newSystemId));
    }

    private async Task LoadCorpAsync(Tracked t, CancellationToken ct)
    {
        var corpId = await esi.GetCharacterCorpIdAsync(t.Auth.CharacterId, ct);
        if (!corpId.HasValue) return;
        var corp = await esi.GetCorporationAsync(corpId.Value, ct);
        t.Snapshot.CorporationId = corpId.Value;
        t.Snapshot.CorporationName = corp?.Name ?? "";
        t.Snapshot.CorporationTicker = corp?.Ticker ?? "";
        PilotChanged?.Invoke(t.Snapshot);
    }

    private async Task<string> GetAccessTokenAsync(Tracked t, CancellationToken ct)
    {
        if (t.AccessToken is not null && DateTimeOffset.UtcNow < t.AccessTokenExpiry - TimeSpan.FromSeconds(60))
            return t.AccessToken;
        try
        {
            var tokens = await sso.RefreshAsync(_clientId, t.Auth.RefreshToken, ct);
            t.AccessToken = tokens.AccessToken;
            t.AccessTokenExpiry = tokens.ExpiresAt;
            if (tokens.RefreshToken != t.Auth.RefreshToken)
            {
                t.Auth = t.Auth with { RefreshToken = tokens.RefreshToken };
                tokenStore.Upsert(t.Auth);
            }
            return tokens.AccessToken;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            StatusChanged?.Invoke(EsiStatus.AuthRequired);
            throw new EsiUnauthorizedException();
        }
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }

    private sealed class Tracked
    {
        public required CharacterAuth Auth { get; set; }
        public required PilotSnapshot Snapshot { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public string? AccessToken { get; set; }
        public DateTimeOffset AccessTokenExpiry { get; set; }
    }
}
