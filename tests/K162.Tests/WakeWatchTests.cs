using K162.Core;
using Xunit;

namespace K162.Tests;

public class WakeWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(10);

    [Fact]
    public void OnSystemLeft_AddsHold_WithExpiry()
    {
        var wake = new WakeWatch();
        wake.OnSystemLeft(31000001, "J130930", "Vex Arkanis", Hold, Now);
        var held = Assert.Single(wake.Held);
        Assert.Equal("J130930", held.SystemName);
        Assert.Equal(Now + Hold, held.ExpiresAt);
        Assert.False(held.Alerted);
    }

    [Fact]
    public void OnSystemLeft_SameSystem_ReplacesEntry()
    {
        var wake = new WakeWatch();
        wake.OnSystemLeft(31000001, "J130930", "Vex Arkanis", Hold, Now);
        wake.OnSystemLeft(31000001, "J130930", "Dren Kovacs", Hold, Now.AddMinutes(5));
        var held = Assert.Single(wake.Held);
        Assert.Equal("Dren Kovacs", held.PilotName);
        Assert.Equal(Now.AddMinutes(15), held.ExpiresAt);
    }

    [Fact]
    public void OnSystemEntered_RemovesHold()
    {
        var wake = new WakeWatch();
        wake.OnSystemLeft(31000001, "J130930", "Vex", Hold, Now);
        wake.OnSystemEntered(31000001);
        Assert.Empty(wake.Held);
    }

    [Fact]
    public void Prune_DropsExpiredHolds_KeepsActive()
    {
        var wake = new WakeWatch();
        wake.OnSystemLeft(31000001, "J130930", "Vex", TimeSpan.FromMinutes(2), Now);
        wake.OnSystemLeft(31000002, "J160941", "Dren", TimeSpan.FromMinutes(20), Now);
        wake.Prune(Now.AddMinutes(5));
        var held = Assert.Single(wake.Held);
        Assert.Equal("J160941", held.SystemName);
    }

    [Fact]
    public void TryAlert_MarksHeldSystem_ReturnsNullOtherwise()
    {
        var wake = new WakeWatch();
        wake.OnSystemLeft(31000001, "J130930", "Vex", Hold, Now);
        var alerted = wake.TryAlert(31000001);
        Assert.NotNull(alerted);
        Assert.True(wake.Held[0].Alerted);
        Assert.Null(wake.TryAlert(31009999));
    }

    [Fact]
    public void Changed_FiresOnMutations()
    {
        var wake = new WakeWatch();
        var fired = 0;
        wake.Changed += () => fired++;
        wake.OnSystemLeft(31000001, "J130930", "Vex", Hold, Now);
        wake.TryAlert(31000001);
        wake.Prune(Now.AddHours(1));
        Assert.Equal(3, fired);
    }
}
