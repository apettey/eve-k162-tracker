# K162 Fleet Intel

A Windows desktop app for EVE Online multiboxers living in wormhole space. It tracks
every online character across your accounts, shows each pilot's current system with
full intel context (recent kills, resident corporations, wormhole statics/effects),
and alerts on kills in systems you just left — so you see problems early while
scanning out.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-blue) ![win--x64](https://img.shields.io/badge/platform-win--x64-lightgrey)

## Features

- **Fleet grid** — one card per online character: system, class, effect, statics,
  48h kill sparkline, threat badge (calm / warm / hot from last-6h kill activity).
- **Focus view** — click a pilot: system header with statics + mass, trail
  breadcrumb, recent kills in system, and a "who lives here" panel that ranks
  resident corps by killmail participation (pilots seen, kill count, active
  timezone, last-seen) over a configurable lookback (48h / 2 weeks / 3 months).
- **Wake Watch** — when a pilot leaves a system it is held for N minutes
  (default 10). A kill landing in a held system fires a toast, a red pulsing
  chip, a double sound ping, and flashes the taskbar when the window is unfocused.
- **Live kill feed** — zKillboard RedisQ long-poll; kills in current systems
  update the intel panels in place.
- **Demo mode** — the full UI with simulated pilots and a SIMULATE JUMP button,
  no login needed (also `K162.App.exe --demo`).

## Data sources

| Source | Used for |
|---|---|
| EVE ESI | character location / online / ship (SSO-scoped), killmail details, names |
| zKillboard | per-system kill history + RedisQ live stream |
| anoik.is static data | bundled J-space database: class, effect, statics (2,604 systems, baked in at build time) |

## First run

1. Create an application at [developers.eveonline.com](https://developers.eveonline.com/)
   with scopes `esi-location.read_location esi-location.read_online esi-location.read_ship_type`
   and callback URL `http://localhost:8410/callback/`.
2. Paste the client id into the setup screen (or Settings), then **ADD CHARACTER**
   for each account — the EVE SSO login opens in your browser.
3. Refresh tokens are stored DPAPI-encrypted for your Windows user under
   `%APPDATA%\K162FleetIntel`.

## Development

```
dotnet build K162FleetIntel.slnx
dotnet test tests/K162.Tests/K162.Tests.csproj
dotnet run --project src/K162.App -- --demo
```

- `src/K162.Core` — net10.0 class library: ESI/SSO/zKillboard clients, wormhole db,
  intel aggregation, Wake Watch state machine (all unit-tested).
- `src/K162.App` — net10.0-windows WPF app (MVVM via CommunityToolkit.Mvvm).

## Releases

CI (`.github/workflows/release.yml`) uses the shared
`apettey/eve-contracts` reusable Windows build workflow. Every push builds a
self-contained win-x64 zip; pushing a `v*` tag additionally builds a **Velopack
installer** (Setup.exe + delta packages) and publishes everything to a GitHub
Release. Installed apps check the release feed on startup and every 6h and show
an update chip in the status bar — click to download and restart.

Cut a release with:

```
.\scripts\release.ps1          # patch bump
.\scripts\release.ps1 minor
.\scripts\release.ps1 1.2.0
```
