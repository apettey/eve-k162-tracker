using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace K162.Core.Sso;

public sealed record SsoTokens(
    long CharacterId, string CharacterName, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// EVE SSO OAuth2 with PKCE for a native app: opens the system browser to the login
/// page, catches the redirect on a localhost HttpListener, and exchanges the code.
/// </summary>
public sealed class SsoService(HttpClient http)
{
    public const string Scopes = "esi-location.read_location.v1 esi-location.read_online.v1 esi-location.read_ship_type.v1";
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize/";
    private const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";

    /// <summary>Runs the interactive login flow. Blocks until the browser round-trip completes or ct fires.</summary>
    public async Task<SsoTokens> LoginAsync(string clientId, int callbackPort, CancellationToken ct)
    {
        var redirectUri = $"http://localhost:{callbackPort}/callback/";
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateVerifier();

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var url = AuthorizeUrl +
            "?response_type=code" +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&scope=" + Uri.EscapeDataString(Scopes) +
            "&code_challenge=" + Pkce.CreateChallenge(verifier) +
            "&code_challenge_method=S256" +
            "&state=" + state;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var contextTask = listener.GetContextAsync();
        var done = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, ct));
        if (done != contextTask) { listener.Stop(); ct.ThrowIfCancellationRequested(); }
        var context = await contextTask;

        var query = context.Request.QueryString;
        var code = query["code"];
        var gotState = query["state"];
        var ok = code is not null && gotState == state;

        var body = Encoding.UTF8.GetBytes(ok
            ? "<html><body style='background:#04080a;color:#3ecf7a;font-family:monospace;padding:40px'>K162 Fleet Intel — authorized. You can close this tab.</body></html>"
            : "<html><body style='background:#04080a;color:#e0563c;font-family:monospace;padding:40px'>K162 Fleet Intel — login failed. Close this tab and retry.</body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body, ct);
        context.Response.Close();
        listener.Stop();

        if (!ok) throw new InvalidOperationException("SSO callback missing code or state mismatch.");

        return await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }, ct);
    }

    public Task<SsoTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken ct) =>
        ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, ct);

    private async Task<SsoTokens> ExchangeAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Host = "login.eveonline.com";
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"SSO token endpoint returned {(int)res.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var refresh = root.GetProperty("refresh_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var (charId, charName) = ParseJwtIdentity(access);
        return new SsoTokens(charId, charName, access, refresh,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>Reads character id and name from the SSO access token JWT payload (no signature validation needed locally).</summary>
    public static (long CharacterId, string Name) ParseJwtIdentity(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new FormatException("Not a JWT.");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        var sub = doc.RootElement.GetProperty("sub").GetString() ?? ""; // "CHARACTER:EVE:2112625428"
        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
        var id = long.Parse(sub[(sub.LastIndexOf(':') + 1)..]);
        return (id, name);
    }
}
