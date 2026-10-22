# EULA compliance review

Audit of K162 Fleet Intel against the EVE Online EULA and CCP's third-party application
policy, covering the concerns that matter for a fleet-intel tool: **reading the screen**,
**reading game memory**, **sending input to the client**, and **automating gameplay**.

**Reviewed:** 2026-09-13, against commit `994d839`.
**Scope:** all shipped source under `src/` (both `K162.Core` and `K162.App`), excluding
build output.

---

## Verdict

**No boundary is crossed. The application is safe to install and run.**

K162 Fleet Intel reads three things: text files the EVE client writes to disk (Local chat
logs), CCP's authenticated ESI web API over the official EVE SSO flow, and the public
zKillboard API. It displays that data and raises alerts. It never touches the EVE client
process, never captures the screen, never sends input, and has no way to act in the game.

The single native call in the codebase flashes **its own** taskbar button when an alert
fires and the window is in the background. It acts on this application's window handle, not
on any EVE window.

---

## The concerns, one by one

### 1. Reading the screen — NOT DONE

There is no screen capture, pixel sampling, or OCR anywhere. Verified by searching all
shipped source for `BitBlt`, `PrintWindow`, `CopyFromScreen`, `GetWindowDC`, `GetPixel`,
and `CaptureScreen` — **zero matches**. The application never sees a pixel of the EVE
client; it does not know where the client's windows are, and does not ask.

### 2. Reading game memory — NOT DONE

No process-memory access of any kind. Verified by searching all shipped source for
`ReadProcessMemory`, `WriteProcessMemory`, `OpenProcess`, `NtReadVirtualMemory`,
`VirtualQueryEx`, `CreateRemoteThread`, and `LoadLibrary` — **zero matches**.

The application never opens a handle to the EVE process and never enumerates processes
(`System.Diagnostics.Process` does not appear in shipped source). The manifest runs
`asInvoker` (standard user, no elevation), so it could not read another process's memory
even if it tried.

### 3. Sending input / automating gameplay — NOT DONE

No input synthesis and no automation. Verified by searching all shipped source for
`SendInput`, `keybd_event`, `mouse_event`, `SendMessage`, `PostMessage`, `SendKeys`, and
`SetWindowsHookEx` — **zero matches**. There is no message pump into another window, no
hotkey registration, and no timer or log line that acts on the game. Every character
position the app shows is *observed*; nothing the app does can move a ship, click a module,
or type into chat. The chat views are read-only — there is no compose or send path.

---

## Complete native API surface

This is the entire list. It is **one function**, and it concerns this application's own
window.

| Function | What it does | Can it affect the game? |
|---|---|---|
| `FlashWindowEx` | flashes *our own* taskbar button when an alert fires and our window is unfocused | no — acts on this app's `WindowInteropHelper` handle; cannot address, move, focus, or read any EVE window |

`FlashWindowEx` is the standard Win32 taskbar-attention API (the same amber flash any chat
app uses for an unread message). It is passed the handle returned by
`WindowInteropHelper(window).Handle` — this application's own top-level window — and returns
a boolean. It cannot enumerate, target, move, focus, or read another process's windows.

A new `DllImport` outside this one-item allow-list fails the build — see
[Enforcement](#enforcement).

---

## Authenticated ESI and EVE SSO — explicitly permitted

Unlike a screen-scraping or memory-reading tool, this app gets its character data from
**CCP's own authenticated API**, exactly as CCP intends third-party tools to.

- **EVE SSO (OAuth 2.0 + PKCE).** Login happens in the user's browser against
  `login.eveonline.com`; the app never sees the user's password. The authorization code is
  returned to a **loopback** listener on `http://localhost:8410/callback/` — nothing is sent
  to any third party.
- **Read-only scopes only.** The app requests exactly three scopes, all read-only:
  `esi-location.read_location.v1`, `esi-location.read_online.v1`,
  `esi-location.read_ship_type.v1`. There is **no write scope**, so no token this app holds
  could modify anything in-game or on a character even if misused.
- **Refresh tokens stay on the machine.** They are encrypted with Windows DPAPI
  (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) under
  `%APPDATA%\K162FleetIntel` and are readable only by the same Windows user. They are never
  uploaded anywhere.
- **The app identifies itself.** Every outbound request carries a descriptive User-Agent —
  `K162FleetIntel/<version> (+https://github.com/apettey/eve-k162-tracker)` — as CCP asks of
  ESI consumers. It does not misrepresent itself.

## Reading the Local chat logs — explicitly permitted

EVE writes chat logs to `Documents\EVE\logs\Chatlogs\Local_*.txt` specifically so players and
tools can read them (the client's "Log Chat to File" option). The app tails the newest Local
log per character to detect a `Channel changed to Local` line and apply the system change a
few seconds faster than ESI polling — which stays on as the authoritative source.

The log is opened **read-only**: `FileMode.Open, FileAccess.Read`, with
`FileShare.ReadWrite | FileShare.Delete`. That permissive *sharing* is required because the
EVE client holds its own write lock on the open log — without it every read would throw. It
grants this app no write access; it never writes, truncates, or deletes an EVE-owned file.

---

## Checked against CCP's prohibitions

| Prohibited | Status | Evidence |
|---|---|---|
| Reading/modifying client memory | **not done** | no memory APIs; no process handle; no elevation |
| Screen scraping / pixel reading / OCR | **not done** | no capture APIs anywhere |
| Injecting code into the client | **not done** | no `CreateRemoteThread`, no `LoadLibrary`, no DLL |
| Automating gameplay / botting | **not done** | no input APIs; the app has no way to act in game |
| Input broadcasting (one keypress → many clients) | **not done** | no `SendInput`, `keybd_event`, `PostMessage`, `SendMessage`, `SendKeys`; no hotkeys |
| Intercepting or modifying network traffic | **not done** | no sockets or packet capture; only HTTPS to the hosts below |
| Modifying client files | **not done** | the only EVE-owned file touched (Local chat log) is opened `FileAccess.Read` |
| Circumventing client restrictions | **not done** | nothing interacts with the client process at all |
| Misrepresenting itself to CCP services | **not done** | ESI requests send an identifying User-Agent; SSO uses the official OAuth flow with read-only scopes |

---

## Data handling

| Operation | Path | Mode |
|---|---|---|
| EVE Local chat logs | `...\EVE\logs\Chatlogs\Local_*.txt` | **read-only** |
| App settings | `%APPDATA%\K162FleetIntel\settings.json` | read/write (app-owned) |
| SSO refresh tokens | `%APPDATA%\K162FleetIntel\tokens.dat` | read/write, **DPAPI-encrypted** (app-owned) |
| Intel / killmail / name caches | `%LOCALAPPDATA%\K162FleetIntel\cache\` | read/write (app-owned) |

The only files the application writes are its own — settings, encrypted tokens, and caches.
Every EVE-owned file is opened for reading only. Nothing from the game logs leaves the
machine.

### Outbound network destinations

All HTTPS. Every host is either CCP's own infrastructure, the public zKillboard API, or the
app's own release feed:

| Host | Purpose |
|---|---|
| `login.eveonline.com` | EVE SSO OAuth (token exchange) |
| `esi.evetech.net` | ESI — character location / online / ship, killmail details, names |
| `images.evetech.net` | ESI image server — character portraits and type renders |
| `zkillboard.com` | public kill history per system |
| `r2z2.zkillboard.com` | public live kill feed (R2Z2) |
| `github.com` | Velopack update feed (GitHub Releases) |
| `localhost` | loopback listener that receives the SSO callback — inbound, not an upload |

`anoik.is` and `developers.eveonline.com` appear in source only as links the user opens in a
browser (the anoik.is system page, the developer-application setup page). The J-space
database itself is generated from anoik.is static data **at build time** and baked into the
app — there is no runtime request to anoik.is.

## Third-party dependencies

`K162.Core` and `K162.App` ship a small, mainstream dependency set (e.g.
`CommunityToolkit.Mvvm` for MVVM, Velopack for updates). None grants prohibited behaviour:
there is no input-synthesis, memory-access, or screen-capture library in the graph.

---

## Enforcement

`tests/K162.Tests/EulaComplianceTests.cs` scans the shipped source at test time and fails
the build if:

- any prohibited API name (memory, screen-capture, or input-synthesis) appears in shipped
  source;
- a new native `DllImport` is declared outside the one-item allow-list (`FlashWindowEx`);
- an EVE-owned file is opened with write access; or
- an outbound URL references a host outside the allow-list above.

A future change that crosses the line breaks `dotnet test` rather than shipping silently.

---

## Conclusion

**Cleared to install.** The app reads CCP's own authenticated API (read-only scopes, over
the official SSO flow), the player's own chat logs (read-only), and the public zKillboard
API. It does not read the screen, does not read memory, and cannot send input or act in the
game. Its entire native footprint is one call that flashes its own taskbar button. Every one
of those claims is enforced by a test in this repository.
