using System.Security.Cryptography;
using System.Text;
using K162.Core;
using K162.Core.Sso;
using Xunit;

namespace K162.Tests;

public class PkceTests
{
    [Fact]
    public void CreateVerifier_Is43CharsBase64Url()
    {
        var verifier = Pkce.CreateVerifier();
        Assert.Equal(43, verifier.Length); // 32 bytes → 43 base64url chars, no padding
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void CreateChallenge_IsSha256OfVerifier()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, Pkce.CreateChallenge(verifier));
    }

    [Fact]
    public void ParseJwtIdentity_ReadsCharacterIdAndName()
    {
        var payload = """{"sub":"CHARACTER:EVE:2112625428","name":"Vex Arkanis"}""";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=');
        var jwt = "eyJhbGciOiJSUzI1NiJ9." + b64 + ".sig";
        var (id, name) = SsoService.ParseJwtIdentity(jwt);
        Assert.Equal(2112625428, id);
        Assert.Equal("Vex Arkanis", name);
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k162-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new SettingsService(dir);
            var settings = new AppSettings
            {
                SoundEnabled = false,
                HoldMinutes = 25,
                Lookback = Lookback.ThreeMonths,
                EsiClientId = "abc123",
                CallbackPort = 9999,
            };
            svc.Save(settings);
            var loaded = svc.Load();
            Assert.False(loaded.SoundEnabled);
            Assert.Equal(25, loaded.HoldMinutes);
            Assert.Equal(Lookback.ThreeMonths, loaded.Lookback);
            Assert.Equal("abc123", loaded.EsiClientId);
            Assert.Equal(9999, loaded.CallbackPort);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingOrCorruptFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k162-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new SettingsService(dir);
            Assert.True(svc.Load().SoundEnabled);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{not json");
            Assert.Equal(10, svc.Load().HoldMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(120, 60)]
    public void HoldMinutes_Clamped(int raw, int expected) =>
        Assert.Equal(expected, new AppSettings { HoldMinutes = raw }.ClampedHoldMinutes);

    [Fact]
    public void Lookback_LabelsAndSpans()
    {
        Assert.Equal("2 WEEKS", Lookback.TwoWeeks.ToLabel());
        Assert.Equal(TimeSpan.FromDays(90), Lookback.ThreeMonths.ToTimeSpan());
        Assert.Equal(TimeSpan.FromHours(48), Lookback.FortyEightHours.ToTimeSpan());
    }
}
