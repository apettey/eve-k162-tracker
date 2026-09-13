using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace K162.Tests;

/// <summary>
/// Enforces the EVE EULA boundary in CI rather than by memory.
///
/// EVE's EULA prohibits reading client memory, capturing the screen, injecting code and
/// automating input. All are absent today; these tests make a future change that introduces
/// one fail the build instead of shipping silently.
///
/// This test scans the shipped source tree on disk (both K162.Core and K162.App) so it covers
/// the WPF app even though the test project only references K162.Core.
///
/// See docs/EULA-COMPLIANCE.md.
/// </summary>
public class EulaComplianceTests
{
    private readonly ITestOutputHelper _output;

    public EulaComplianceTests(ITestOutputHelper output) => _output = output;

    /// <summary>Repo root: walk up from the test binary until we find the solution file.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "K162FleetIntel.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Shipped source only - test files may legitimately mention these names.</summary>
    private static IEnumerable<string> ShippedSourceFiles()
    {
        var src = Path.Combine(RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    public static TheoryData<string, string> ProhibitedApis() => new()
    {
        // Reading the game's memory.
        { "ReadProcessMemory", "reads another process's memory" },
        { "WriteProcessMemory", "writes another process's memory" },
        { "OpenProcess", "opens a handle to another process" },
        { "NtReadVirtualMemory", "reads another process's memory" },
        { "VirtualQueryEx", "inspects another process's memory layout" },
        { "CreateRemoteThread", "injects code into another process" },
        { "LoadLibrary", "injects a library into another process" },

        // Capturing the screen.
        { "BitBlt", "captures screen content" },
        { "PrintWindow", "captures a window's rendered content" },
        { "CopyFromScreen", "captures screen content" },
        { "GetWindowDC", "obtains a device context for reading pixels" },
        { "GetPixel", "samples screen pixels" },

        // Sending input to the game, including multi-client broadcasting, and automation.
        { "SendInput", "synthesises input events" },
        { "keybd_event", "synthesises keyboard input" },
        { "mouse_event", "synthesises mouse input" },
        { "SendMessage", "can deliver input to another window" },
        { "PostMessage", "can deliver input to another window" },
        { "SendKeys", "synthesises keystrokes" },
        { "SetWindowsHookEx", "installs a system-wide hook" },
    };

    [Theory]
    [MemberData(nameof(ProhibitedApis))]
    public void ShippedCodeNeverReferencesAProhibitedApi(string api, string why)
    {
        var root = RepoRoot();
        var offenders = ShippedSourceFiles()
            .Where(path => File.ReadAllText(path).Contains(api, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"'{api}' ({why}) appears in: {string.Join(", ", offenders)}. " +
            "This would breach the EVE EULA - see docs/EULA-COMPLIANCE.md.");
    }

    /// <summary>
    /// The native surface is an allow-list of exactly one call. A new DllImport outside it
    /// fails here, which forces a deliberate decision rather than an accidental capability.
    /// </summary>
    [Fact]
    public void NativeApiSurfaceIsExactlyTheApprovedSet()
    {
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            // Flashes THIS app's own taskbar button when an alert fires while the window is
            // unfocused. It is handed our own WindowInteropHelper handle and returns a bool;
            // it cannot target, move, focus or read any EVE window. See docs/EULA-COMPLIANCE.md.
            "FlashWindowEx",
        };

        var declared = new List<string>();
        var pattern = new Regex(@"static\s+extern\s+[\w\.\<\>\[\]\?]+\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

        foreach (var path in ShippedSourceFiles())
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(path)))
                declared.Add(m.Groups["name"].Value);
        }

        foreach (var name in declared.Distinct().OrderBy(n => n))
            _output.WriteLine($"declared native call: {name}");

        var unexpected = declared.Where(n => !approved.Contains(n)).Distinct().ToList();

        Assert.True(unexpected.Count == 0,
            $"Unapproved native call(s): {string.Join(", ", unexpected)}. " +
            "A new native call must be a deliberate, documented decision - see docs/EULA-COMPLIANCE.md.");
    }

    /// <summary>
    /// Files the EVE client owns must only ever be opened for reading. FileShare.ReadWrite
    /// is required (EVE holds a write lock on an open log) but must never be paired with
    /// write access.
    /// </summary>
    [Fact]
    public void EveOwnedFilesAreOpenedReadOnly()
    {
        var root = RepoRoot();
        foreach (var path in ShippedSourceFiles())
        {
            var text = File.ReadAllText(path);
            var rel = Path.GetRelativePath(root, path);

            foreach (Match m in Regex.Matches(text, @"new FileStream\((?<args>[^;]*?)\)"))
            {
                var args = m.Groups["args"].Value;
                Assert.False(args.Contains("FileAccess.Write") || args.Contains("FileAccess.ReadWrite"),
                    $"{rel} opens a FileStream with write access: {args.Trim()}. " +
                    "EVE-owned logs must be read-only - see docs/EULA-COMPLIANCE.md.");
            }
        }
    }

    /// <summary>
    /// The only outbound hosts are CCP's own services, the public zKillboard API, and the
    /// app's own release feed. Anything else could mean data leaving the machine.
    /// </summary>
    [Fact]
    public void TheOnlyOutboundHostsAreExpected()
    {
        var allowedHosts = new HashSet<string>(StringComparer.Ordinal)
        {
            // CCP infrastructure.
            "login.eveonline.com",       // EVE SSO OAuth
            "esi.evetech.net",           // ESI API
            "images.evetech.net",        // ESI image server (portraits / type renders)
            "developers.eveonline.com",  // link the user opens to create an ESI application

            // zKillboard public API.
            "zkillboard.com",            // per-system kill history
            "r2z2.zkillboard.com",       // live kill feed (R2Z2)

            // The app's own release feed and repository.
            "github.com",                // Velopack update feed (GitHub Releases) / repo link

            // Static wormhole data - opened only in a browser as a link; the DB is baked in
            // at build time, so there is no runtime request here.
            "anoik.is",

            // Loopback listener that receives the SSO callback (inbound, not an upload).
            "localhost",
        };

        // Documentation links in comments are not requests.
        var docHosts = new HashSet<string>(StringComparer.Ordinal)
        {
            "dotnet.microsoft.com", "learn.microsoft.com", "schemas.microsoft.com",
            "docs.microsoft.com", "github.io", "keepachangelog.com", "semver.org",
        };

        var root = RepoRoot();
        foreach (var path in ShippedSourceFiles())
        {
            var rel = Path.GetRelativePath(root, path);
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"https?://(?<host>[\w\.\-]+)"))
            {
                var host = m.Groups["host"].Value;
                if (docHosts.Contains(host)) continue;

                Assert.True(allowedHosts.Contains(host),
                    $"{rel} references unexpected host '{host}'. " +
                    "Add it to the allow-list deliberately - see docs/EULA-COMPLIANCE.md.");
            }
        }
    }
}
