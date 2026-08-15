# DeskBar — per-desktop focus bar

## What it is

A small always-running Windows utility that pins an information bar to the upper-right corner
of the screen. Each Windows virtual desktop gets its own bar identity (title, quick note,
color), so at a glance you always know which project a desktop belongs to — before you type a
prompt into the wrong window.

Visual reference: `references/final-states.jpg` (chosen direction), full exploration in
`references/comps.html`. (The `references/` design comps are kept out of the public repo —
they contain real account data; see `docs/screenshot.png` for the shipped look.)

## Problem it solves

The workflow this serves: one project per virtual desktop (4-finger swipe between them). Links open on
the wrong desktop, desktops lose their identity, and prompts get typed into the wrong project's
terminal. The bar gives every desktop a permanent, color-coded label plus one-click tools.

## Chosen design (from comps, revised after first real use)

- **Look**: dark glassy HUD — translucent near-black panel (#0B1220 ~92% opacity),
  rounded 12px corners, 1.5px accent border in the desktop's color (default JITR sky-blue
  #0ea5e9). Title and colors follow `references/comp-1-dark-hud.jpg`. Always on top, always
  fully opaque; does not reserve screen space.
- Launcher/controls are compact icon-only buttons (~50% of the comp size, names in
  tooltips) so the title/note/sessions text gets the width.
- Dropped after use: the idle fade (comp's 30% ghost state) and loud mode (the paint-bucket
  full-tint toggle) — neither proved useful in practice (2026-08-11 feedback).

## Geometry

- Position: top-right corner of the primary screen, small margin (8px).
- Size: half the screen width; height ~100 DIPs (= ~150 physical px at 150% scale).
- Both stored in the config file (`widthFraction`, `barHeightDip`) so they can be tuned.

## Bar contents (left to right)

1. **Title** — large, accent-colored, editable via pencil icon (inline textbox, Enter commits,
   Esc cancels).
2. **Quick note** — one dim line under the title, editable the same way.
3. **Sessions line** — small third line, auto-filled: names of Claude sessions detected on
   *this* desktop (see "Session detection"). Read-only, disappears when nothing detected.
4. **Launchers** — outline-icon buttons (no emojis):
   - **Terminals**: launches two `wt.exe` windows and positions them side by side, each half
     the work area (same Win32 MoveWindow technique as the existing
     Win32 launch-and-MoveWindow technique, split left/right).
   - **Chrome**: new Chrome window.
   - **Edge**: new Edge window.
5. **Color swatch** — opens a preset palette (10 colors) + "Custom..." (standard color dialog).
6. **Paint-bucket** — toggles loud mode for this desktop.
7. **Account + limits** — right block:
   - Claude account email, read from `~/.claude.json` → `oauthAccount.emailAddress`.
   - Three meters — **Session** (5-hour), **Weekly** (all models), **Fable** (model-scoped
     weekly) — used% fill with used/remaining tooltip and reset time.

Right-click anywhere on the bar: context menu with Refresh, Start with Windows (toggle),
and Exit.

## Data sources

| Data | Source | Notes |
|---|---|---|
| Account email | `~/.claude.json` `oauthAccount.emailAddress` | re-read every 5 min |
| Limits (Session/Weekly) | `rate_limits` in the newest `~/.claude/jitr-status-<sessionId>.json` statusline tee file | local, zero network; tee files older than 24h -> "n/a (no recent claude session)" |
| Limits (per-model row; all rows without tee files) | `GET https://api.anthropic.com/api/oauth/usage`, Bearer token from `~/.claude/.credentials.json` (`claudeAiOauth.accessToken`) | the undocumented endpoint behind Claude Code's `/usage` screen; kinds `session`, `weekly_all`, `weekly_scoped`. Rate-limits the whole ACCOUNT into persistent 429s under real polling, so it is fetched at most once per 10 min machine-wide via the shared claim/cache file `~/.claude/jitr-usage-endpoint.json` ({attemptedAt, fetchedAt, lastStatus, rows:[{kind,label,used,resets}]}; stamp `attemptedAt` before requesting; failures keep last-good rows). jitr-term/jitr-lite honor the same file. See README "Usage meters and rate limits". Every failure renders "n/a", never an error |
| Sessions on this machine | `~/.claude/jitr-status-<sessionId>.json` files written by the statusline tee | fresh = `receivedAt` within 2 min; carries `session_name`, `cwd`, `model` |

## Session detection (per desktop)

The status files say which sessions are alive but not which desktop they're on. Mapping:

1. Enumerate visible top-level windows on the **current** virtual desktop
   (`EnumWindows` + `IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop`).
2. Collect window titles.
3. A fresh session belongs to this desktop if any such title contains its `session_name` or
   the basename of its `cwd` (terminal titles carry one or the other).
4. Matched names render on the sessions line, comma-separated, truncated to fit.

Fail-soft: no match, no line. This is a heuristic v1; if it proves unreliable the fallback
plan is a terminal-side helper that registers a window handle per session.

## Per-desktop behavior (core mechanism)

The bar is a WS_EX_TOOLWINDOW window (that's what keeps it out of the taskbar and alt-tab),
and Windows shows tool windows on **every** virtual desktop. So a single bar window is always
visible everywhere and never moves — only its **content** swaps when the desktop changes:

- Switch detection: the shell rewrites
  `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\CurrentVirtualDesktop`
  (a 16-byte GUID) on every desktop switch. A 400ms poll reads it; when the GUID changes,
  the bar loads that desktop's profile.
- `IsWindowOnCurrentVirtualDesktop(bar)` is useless for this — for a tool window it always
  answers "yes" (that bug shipped first: one profile was silently shared by every desktop).
  The COM interface is still used to place *other* windows for the sessions line.
- Profiles are keyed by desktop GUID and persisted to `~/.jitr/deskbar.json`:
  `{ "desktops": { "<guid>": { "title", "note", "color", "loud" } }, "widthFraction", "barHeightDip", "customLeft"/"customTop" (only after a drag) }`.
- A brand-new desktop gets a default profile ("Desktop", sky-blue) on first visit.

No build-specific undocumented desktop APIs — one documented COM interface plus one
long-stable registry value.

## Tech stack

**C# on .NET Framework 4.8, WPF built entirely in code (no XAML), compiled by the in-box
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.**

Why: no .NET SDK is installed on this machine and the in-box compiler needs zero installs;
.NET Framework 4.8 ships with Windows 11, so the output is a single small native-feeling
`jitr-deskbar.exe` with instant startup — right for an always-running utility. The compiler
is C# 5 (no string interpolation, no `?.`), which the code respects. WPF gives the rounded
translucent panel, opacity animation, and vector outline icons that match the comp.

- `src/*.cs` — source (window, virtual desktop interop, profiles, usage, sessions, launchers)
- `build.ps1` — one-shot compile to `bin/jitr-deskbar.exe`
- References: PresentationFramework, PresentationCore, WindowsBase, System.Xaml,
  System.Web.Extensions (JSON), System.Windows.Forms + System.Drawing (color dialog, tray-free)

## Out of scope for v1

- Multi-monitor (bar on primary screen only)
- Per-desktop wallpaper/color sync with Windows itself
- Moving/resizing the bar by mouse (config values instead)
- session → window-handle registry (only if title matching proves too weak)
