# Handoff: K162 Fleet Intel Tracker

## Overview
A Windows desktop application for EVE Online multiboxers living in wormhole space. It tracks every online character across accounts, shows each pilot's current system with full intel context (recent kills, resident corporations, wormhole statics/effects), and alerts on kills in recently-vacated systems. Data sources: EVE ESI (character location/online/ship), zKillboard (killmails, RedisQ live stream), Anoik.is (wormhole system metadata).

## Target platform
**.NET 8.0, WPF, win-x64.** The bundled file is a **design reference created in HTML** — a prototype showing intended look and behavior, not production code. Recreate this design in WPF (or WinUI 3 if preferred) using its idioms: XAML styles/templates for the visual system, MVVM for state, `HttpClient` + `System.Text.Json` for the APIs.

## Fidelity
**High-fidelity.** Colors, typography, spacing, and interactions are final intent. Recreate pixel-perfectly.

## Architecture (real app)
- **Auth**: EVE SSO OAuth2 with PKCE per character. Open system browser to login URL, catch callback on localhost listener, store refresh tokens encrypted (DPAPI). Scopes: `esi-location.read_location`, `esi-location.read_online`, `esi-location.read_ship_type`.
- **Location polling**: ESI `/characters/{id}/location/` every ~5s per online character (respect ESI cache timers + error budget).
- **Live kills**: zKillboard RedisQ (`https://zkillboard.com/api/redisq.php` successor endpoint) long-poll loop; filter incoming killmails against (a) systems characters are currently in, (b) the Wake Watch held-system list.
- **System intel on jump**: zKillboard API for recent kills + killmail history in system; aggregate attacker corps over the lookback window to derive "who lives here"; Anoik.is / static wormhole data for class, effect, statics.
- **Resident heuristic**: over the lookback window (48h / 2 weeks / 3 months, user setting), rank corporations by killmail participation count in that system; show pilots seen, kill count, active timezone (from kill timestamp histogram), last-seen.

## Screens / Views

### 1. Window chrome
- Custom-drawn Windows title bar, 34px tall, background `#10161a`, bottom border 1px `#1c2529`.
- Left: 14px green angular logo mark (1px `#3ecf7a` border, corner-cut), title "K162 Fleet Intel — Cold Static [CSTAT]" in Share Tech Mono 11px `#8aa695`.
- Right: min/max/close buttons 46px wide, full height; hover `#1c2529` (close hover `#c42b1c`, white glyph).
- Window body background `#04080a` with a faint radial green glow at top (`radial-gradient(ellipse 80% 60% at 50% -10%, rgba(62,207,122,0.06), transparent)`).
- Status bar, 26px, background `#10161a`, top border `#1c2529`, Share Tech Mono 10px `#3d5548`: ".NET 8.0 · WPF · win-x64", "ESI: CONNECTED" (value in `#3ecf7a`), "zKillboard RedisQ: LISTENING", right-aligned "TQ TIME hh:mm".

### 2. Top app bar
- 14px vertical padding, 24px horizontal, bottom border 1px `rgba(62,207,122,0.18)`.
- "K162" Share Tech Mono 20px `#3ecf7a` letter-spacing 2px, "FLEET INTEL" Rajdhani 13px uppercase letter-spacing 4px `#5c7a68`.
- Right: "N PILOTS ONLINE" (Share Tech Mono 12px `#5c7a68`) and an accent button ("SIMULATE JUMP" in the prototype — in the real app this space holds settings): Share Tech Mono 11px, `#3ecf7a` on `rgba(62,207,122,0.08)`, 1px border `rgba(62,207,122,0.4)`, 8px corner-cut clip-path, hover background `rgba(62,207,122,0.18)`.

### 3. Wake Watch strip (conditional)
- Appears under the app bar when any system is held. Background `rgba(62,207,122,0.02)`, bottom border `rgba(62,207,122,0.12)`, 8px/24px padding.
- Label "WAKE WATCH" Share Tech Mono 10px letter-spacing 2px `#5c7a68`; helper text "kills in systems you left < N min ago trigger an alert" in `#3d5548`.
- One chip per held system: Share Tech Mono 10px, 2px/8px padding, 1px border `rgba(62,207,122,0.25)`, text `#8aa695`: "J130930 · Vex · 6:23" (system, pilot first name, mm:ss countdown). After an alert fires the chip turns `#e0563c` (border + text) and pulses (opacity 1→0.45, 1.2s loop).
- Behavior: when a character leaves a system it is added with expiry = now + hold time (setting, default 10 min, range 1–60). Chips count down each second and disappear at expiry. Re-entering a held system replaces its entry.

### 4. Grid view (default) — one card per online character
- CSS-grid equivalent: auto-fit columns min 430px, 14px gap, 18px/24px padding, max content width 1500px centered.
- Card: background `rgba(8,14,11,0.7)`, 1px border `rgba(62,207,122,0.22)`, 12px corner-cut clip-path (top-left and bottom-right), cursor pointer; hover border `rgba(62,207,122,0.6)` + background `rgba(62,207,122,0.04)`. Click focuses the pilot.
- **Jump flash**: when the pilot jumps, the card flashes — border to `rgba(62,207,122,0.9)` and an animated glow (`box-shadow 0 0 40px rgba(62,207,122,0.55)` + inset, easing out over 1.5s) — with a short 880Hz sine ping (~0.4s, exponential decay), if sound enabled.
- Card header row (12px/14px padding, bottom border `rgba(62,207,122,0.12)`): 44px character portrait (EVE image server `characters/{id}/portrait`, 1px border `rgba(62,207,122,0.3)`), name Rajdhani 17px/600 `#dff2e6`, ship Share Tech Mono 11px `#5c7a68`, threat badge right.
- **Threat badge**: Share Tech Mono 11px uppercase letter-spacing 2px, 3px/10px padding, 1px border in threat color, text same color. calm `#3ecf7a` / warm `#d6a52a` / hot `#e0563c` (hot pulses). Computed from kill count in the last 6h of the activity histogram: ≥6 hot, ≥2 warm, else calm.
- System line: system name Share Tech Mono 24px `#3ecf7a`, class ("C4", "C5", "C-SHATTERED") Rajdhani 13px/600 `#8aa695`, wormhole effect ("Wolf-Rayet", "Pulsar") 12px `#d6a52a`.
- Statics chips: Share Tech Mono 10px `#8aa695`, 1px border `rgba(62,207,122,0.22)`, "X877 → C4".
- Sparkline: 24 bars (48h in 2h bins), 34px tall, 2px gap; active bars `rgba(62,207,122,0.75)`, zero bars `rgba(62,207,122,0.12)` at 6% min height.
- Footer: "N KILLS / 48H" and "N RESIDENT GROUPS", Share Tech Mono 11px `#5c7a68`.

### 5. Focus view (click a card)
Two-column grid: 250px rail + fluid main, 16px gap.

**Left rail**
- "◄ ALL PILOTS" back button: Share Tech Mono 11px, 1px border `rgba(62,207,122,0.3)`, text `#8aa695`, hover green.
- One row per pilot: 34px portrait, name 14px/600 `#dff2e6`, "system · ship" Share Tech Mono 11px `#5c7a68`, 8px threat dot right (pulses when hot). Selected row: border `rgba(62,207,122,0.6)`, background `rgba(62,207,122,0.06)`. Click switches focus.

**System header panel**
- 1px border `rgba(62,207,122,0.3)`, background `rgba(62,207,122,0.03)`, 14px corner-cut clip-path, 16px/18px padding. A 1px scan line sweeps along the top edge (gradient segment, 4s linear loop).
- Pilot identity row (bottom-bordered): 56px portrait, 32px corp logo (`corporations/{id}/logo`), pilot name 19px/600, "corp · ship" Share Tech Mono 11px `#5c7a68`.
- System row: system name Share Tech Mono 34px `#3ecf7a` letter-spacing 2px; class 16px/600 `#8aa695`; effect 14px `#d6a52a`; "THREAT: level" badge right.
- Statics row incl. mass ("X877 → C4 2.0G"); right-aligned link buttons "ZKILLBOARD ↗" and "ANOIK.IS ↗" (Share Tech Mono 11px, bordered) → `https://zkillboard.com/system/{name}/` and `http://anoik.is/systems/{name}`.
- Trail row: "TRAIL" label + breadcrumb "J130930 › J144208 › J121406", past systems `#5c7a68`, current `#3ecf7a`.

**Recent kills panel** (left, 1.2fr)
- Panel: 1px border `rgba(62,207,122,0.18)`, background `rgba(10,18,14,0.6)`. Header "RECENT KILLS IN SYSTEM" Rajdhani 13px letter-spacing 3px `#8aa695` / "LAST 48H" right.
- Activity graph: 24 bars, 52px tall, 3px gap, same coloring as sparkline; axis labels "-48H / -24H / NOW" 9px `#3d5548`.
- Kill rows: Share Tech Mono 12px — time `#5c7a68` (52px), 20px victim-corp logo, ship `#dff2e6` (96px), victim corp `#8aa695` (flex), "N×" attacker count, "zkill ↗" link.

**Who lives here panel** (right, 1fr)
- Header "WHO LIVES HERE" + lookback label ("2 WEEKS", setting).
- Corp rows: 32px corp logo; name 14px/600 `#dff2e6` + ticker `[HPNCH]` Share Tech Mono 11px `#3ecf7a`; "zkill ↗" link right; meta line Share Tech Mono 11px `#5c7a68`: "12 pilots seen · 41 kills · active EU · last 2h ago" (last-seen "now" renders `#e0563c`).
- Footer note: "Derived from N killmails over LOOKBACK" 11px `#3d5548`.

### 6. Kill-in-wake toast
- Fixed top-right, 320px, background `#140a08`, 1px border `#e0563c`, red glow shadow, 10px corner-cut, flash-in animation.
- Header: "⚠ KILL IN WAKE" Share Tech Mono 11px `#e0563c` pulsing; ✕ dismiss.
- Body: system name Share Tech Mono 16px `#dff2e6`; detail "Astero destroyed · 5 attackers" 13px; footer "Vex Arkanis's wake" + red "zkill ↗" link.
- Fires with a double sound ping; auto-dismisses after 9s. In the real app also raise a Windows toast notification when the window is unfocused.

## Interactions & Behavior
- Card click → focus view; rail click → switch pilot; back → grid.
- Jump event → card flash (1.5s ease-out) + single ping; system left enters Wake Watch.
- Kill in held system → toast + chip turns red/pulsing + double ping.
- All hover states listed per component above. External links open in default browser.

## State Management
- `characters[]`: id, name, portraitId, corpId, ship, currentSystem, trail (last 4 systems), online.
- `systemIntel` (per system, cached): class, effect, statics[{code, destClass, massLimit}], killBins[24] (2h bins/48h), recentKills[], residents[{corp, ticker, corpId, pilotsSeen, kills, tz, lastSeen}], killsAnalyzed.
- `heldSystems[]`: {system, pilot, expiresAt, alerted} — pruned every second.
- `toast`: nullable current alert.
- Settings: `soundEnabled` (bool, default true), `holdMinutes` (1–60, default 10), `lookback` (48 HOURS / 2 WEEKS / 3 MONTHS, default 2 WEEKS).

## Design Tokens
- Background: window desk `#0d1114`, app `#04080a`, panel `rgba(10,18,14,0.6)`, card `rgba(8,14,11,0.7)`, chrome `#10161a`.
- Accent green `#3ecf7a`; borders at alpha 0.12/0.18/0.22/0.3/0.4/0.6 of the accent.
- Threat: calm `#3ecf7a`, warm `#d6a52a`, hot `#e0563c`. Close-button hover `#c42b1c`.
- Text: bright `#dff2e6`, body `#b8cfc2`, secondary `#8aa695`, muted `#5c7a68`, faint `#3d5548`.
- Type: **Rajdhani** (400–700) for UI labels/names; **Share Tech Mono** for all data (systems, times, counts, chips). Both on Google Fonts.
- Corner-cut clip-path (signature shape): `polygon(Npx 0, 100% 0, 100% calc(100% - Npx), calc(100% - Npx) 100%, 0 100%, 0 Npx)` with N = 8–14.
- Animations: jumpflash (glow decay 1.5s), hotpulse (opacity 1→0.45, 1.2s loop), scan sweep (4s linear loop).

## Assets
- Character portraits: `https://images.evetech.net/characters/{id}/portrait?size=64`
- Corp logos: `https://images.evetech.net/corporations/{id}/logo?size=64`
- No other imagery; the prototype uses placeholder IDs (render as defaults) — real IDs come from ESI.

## Files
- `K162 Intel Tracker.dc.html` — the full interactive prototype (grid view, focus view, Wake Watch, toast, simulated jump/kill demo). All mock data lives in its script block.
