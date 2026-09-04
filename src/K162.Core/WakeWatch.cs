namespace K162.Core;

/// <summary>
/// The Wake Watch: systems a pilot recently left, held for a configurable time.
/// Kills arriving in a held system trigger an alert. Pure state machine — unit tested.
/// </summary>
public sealed class WakeWatch
{
    private readonly List<HeldSystem> _held = [];

    public event Action? Changed;

    public IReadOnlyList<HeldSystem> Held => _held;

    /// <summary>Called when a pilot leaves a system. Replaces any existing hold on the same system.</summary>
    public void OnSystemLeft(int systemId, string systemName, string pilotName, TimeSpan holdTime, DateTimeOffset now)
    {
        _held.RemoveAll(h => h.SolarSystemId == systemId);
        _held.Add(new HeldSystem
        {
            SolarSystemId = systemId,
            SystemName = systemName,
            PilotName = pilotName,
            ExpiresAt = now + holdTime,
        });
        Changed?.Invoke();
    }

    /// <summary>Called when a pilot enters a system — a hold there is pointless while someone is on grid.</summary>
    public void OnSystemEntered(int systemId)
    {
        if (_held.RemoveAll(h => h.SolarSystemId == systemId) > 0)
            Changed?.Invoke();
    }

    /// <summary>Drops expired holds. Call once a second.</summary>
    public void Prune(DateTimeOffset now)
    {
        if (_held.RemoveAll(h => h.ExpiresAt <= now) > 0)
            Changed?.Invoke();
    }

    /// <summary>Marks a held system alerted (kill seen); returns it, or null when the system isn't held.</summary>
    public HeldSystem? TryAlert(int systemId)
    {
        var held = _held.FirstOrDefault(h => h.SolarSystemId == systemId);
        if (held is null) return null;
        held.Alerted = true;
        Changed?.Invoke();
        return held;
    }
}
