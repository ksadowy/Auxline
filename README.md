# Auxline

A lightweight, customizable media widget for the Windows 11 taskbar. It shows the current track (cover art, title, artist) in the empty area on the left side of the taskbar. Clicking it opens a flyout with playback controls and volume.

Works with Spotify and any other app that reports to Windows media controls (browsers, YouTube, etc.). No Spotify API keys required.

---

## Features

- [x] Compact widget on the taskbar: cover thumbnail, title, artist (never covers taskbar icons)
- [x] Flyout on click: large cover art, play/pause, next/previous, volume slider
- [x] Per-app volume control (changes the playing app's volume only, not system volume)
- [x] Progress bar / seek (if the player reports timeline)
- [x] Source selection: choose which apps (Spotify, browsers, etc.) the widget connects to, and their priority
- [x] Customization: position, size, colors, fonts, acrylic/blur, corner style
- [x] Auto-hide when a fullscreen app or game is running
- [x] Multi-monitor: choose which monitor's taskbar (per-monitor DPI-aware)
- [x] Start with Windows
- [x] Tray icon with settings and exit

---

## Tech stack

| Area | Choice |
|---|---|
| Language / UI | C# (.NET 10), WPF |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| Media info & controls | `Windows.Media.Control` (Global System Media Transport Controls, GSMTC) |
| Per-app volume | Windows Core Audio API via [NAudio](https://github.com/naudio/NAudio) |
| Window placement | Win32 interop (`user32.dll`, `shell32.dll`) |
| Settings | JSON file in `%AppData%\Auxline\` |

**Target resource usage:** ~30–60 MB RAM, a few MB VRAM, near-zero CPU when idle (event-driven, no polling).

---

## Requirements

- Windows 11 (Windows 10 1809+ should work for media APIs, but taskbar layout differs)
- .NET 10 SDK
- Visual Studio 2026 or VS Code with C# Dev Kit

The project must target a Windows SDK version to access WinRT APIs:

```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<UseWPF>true</UseWPF>
```

---

## Architecture

```
Auxline/
├── App.xaml / App.xaml.cs        # Startup, single instance, tray icon
├── Diagnostics/
│   ├── AppDiagnostics.cs         # Log file + global error handlers
│   └── RollingFileTraceListener.cs # Trace → %LocalAppData%\Auxline\logs (1 MB, 1 backup)
├── Models/
│   ├── MediaTrack.cs             # Immutable snapshot of the current track
│   └── MediaTimeline.cs          # Position/duration + interpolation
├── ViewModels/
│   └── PlayerViewModel.cs        # Now-playing state + commands, shared by all windows
├── Services/
│   ├── MediaService.cs           # GSMTC: current session, track info, controls
│   ├── VolumeService.cs          # Core Audio: per-app volume
│   ├── AppIdentity.cs            # Maps GSMTC app ID → process name
│   ├── TaskbarService.cs         # Taskbar position, fullscreen detection
│   ├── AutostartService.cs       # Start with Windows (Run key + Task Manager toggle)
│   └── SettingsService.cs        # Load/save JSON settings
├── Views/
│   ├── DebugWindow.xaml          # Phase 1: track info + controls, for testing
│   ├── WidgetWindow.xaml         # Compact widget sitting on the taskbar
│   ├── FlyoutWindow.xaml         # Popup with cover art and controls
│   ├── AppTrayIcon.cs            # Tray icon + native menu
│   └── SettingsWindow.xaml       # Customization UI
├── Interop/
│   ├── NativeMethods.cs          # P/Invoke declarations
│   └── WindowBackdrop.cs         # Acrylic, rounded corners, dark frame
└── app.manifest                  # PerMonitorV2 DPI awareness
```

---

## Roadmap

### Phase 1 - Media core
- [x] Get `GlobalSystemMediaTransportControlsSessionManager` via `RequestAsync()`
- [x] Pick the session: playing Spotify (`SourceAppUserModelId` contains `Spotify`) → any playing session → Windows' current session
- [x] Read title, artist, album via `TryGetMediaPropertiesAsync()`
- [x] Load the cover from `Thumbnail` (`OpenReadAsync()` → `BitmapImage`)
- [x] Subscribe to events instead of polling:
  - `SessionsChanged`, `CurrentSessionChanged`
  - `MediaPropertiesChanged`, `PlaybackInfoChanged`
- [x] Controls: `TryTogglePlayPauseAsync()`, `TrySkipNextAsync()`, `TrySkipPreviousAsync()`
- [x] Test with a simple debug window

### Phase 2 - Volume
- [x] Enumerate audio sessions with NAudio (`MMDeviceEnumerator` → `AudioSessionManager`)
- [x] Find the current source's audio session by process name (derived from `SourceAppUserModelId`: resolve the exe via `shell:AppsFolder`, which handles hashed IDs of Firefox-based browsers; fall back to parsing the ID, e.g. `Spotify.exe` → `Spotify`)
- [x] Get/set volume via `SimpleAudioVolume`
- [x] Handle Spotify restarting (session disappears and reappears)

### Phase 3 - Taskbar widget
- [x] Borderless, transparent window (`WindowStyle=None`, `AllowsTransparency=True`)
- [x] Get taskbar rect via `SHAppBarMessage(ABM_GETTASKBARPOS)`
- [x] Position the widget on the left side of the taskbar (offset is a constant for now; becomes a setting in Phase 5)
- [x] Extended window styles:
  - `WS_EX_TOOLWINDOW`: hide from Alt+Tab and taskbar
  - `WS_EX_NOACTIVATE`: don't steal focus when clicked
  - `WS_EX_TOPMOST`: stay above the taskbar
- [x] Keep the widget above the taskbar (the taskbar raises itself on clicks, Start, snipping overlay):
  - Owned by the taskbar (`GWLP_HWNDPARENT` = `Shell_TrayWnd`), so Windows raises it together with the taskbar
  - Re-assert topmost 300 ms after each foreground change (`EVENT_SYSTEM_FOREGROUND`), for shell raises that ignore ownership
  - Re-attach on `TaskbarCreated` (Explorer restart)
- [x] Widget sizes to its content (max 240 DIP), so empty space stays clickable for the taskbar
- [x] Reposition on display/DPI changes (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`)
- [x] Text color follows the taskbar theme (`SystemUsesLightTheme`)

### Phase 4 - Flyout
- [x] Open the flyout above the widget on click
- [x] Large cover art, track info, controls, volume slider
- [x] Disable previous/next when the player doesn't support them (`PlaybackInfo.Controls`)
- [x] Close on focus loss or Esc
- [x] Optional: acrylic background (`DwmSetWindowAttribute`), solid fallback before Windows 11 22H2
- [x] Optional: progress bar + seek via `TryChangePlaybackPositionAsync()`
  - Subscribe to `TimelinePropertiesChanged` for position updates
  - Note: Spotify updates timeline info irregularly (~4.5 s); interpolate position locally between updates
  - Ticks only while the flyout is open and playing; seeks once on release (click or drag)

### Phase 5 - Polish
- [x] Hide when a fullscreen app is running
  - App bar registration (`ABM_NEW`, no `ABM_SETPOS`, so no reserved space) → `ABN_FULLSCREENAPP` open/close notifications
  - `SHQueryUserNotificationState` at startup and after foreground changes as a safety net
- [x] Hide when nothing is playing (content collapses, window shrinks to zero width)
- [x] Handle taskbar auto-hide
  - Windows 11 moves the taskbar window itself (jumps back when shown, slides down ~280 ms when hidden, 2 px stay on screen)
  - `EVENT_OBJECT_LOCATIONCHANGE` hook limited to Explorer's process; not at its docked position = hidden → widget hides
  - Hiding (instead of sliding along) keeps the 2 px edge free for the mouse to bring the taskbar back
- [x] Taskbar edge (Windows 11 only docks at the bottom without hacks/tools)
  - Top: flyout opens below the widget
  - Left/right: cover only (no title/artist) in a cell as wide as the taskbar and as tall as the cover + margins (fits the gap between apps and tray), placed top→bottom in a free gap; flyout opens beside it, kept on screen
- [x] React to taskbar moves and alignment changes immediately (`WM_SETTINGCHANGE`), re-measure once icons settle
- [x] `TaskbarAttachment` class: ownership, topmost, app bar notifications, fullscreen/auto-hide state (split out of `WidgetWindow`)
- [x] Multi-monitor support (choose which taskbar)
  - All taskbars: `Shell_TrayWnd` + `Shell_SecondaryTrayWnd` windows; docked bounds/edge derived from window + monitor rects (`TaskbarGeometry.Dock`), since the app bar query only knows the primary
  - Setting: monitor device (e.g. `\\.\DISPLAY1`), null = primary; an unplugged monitor falls back to the primary taskbar
  - Ownership, auto-hide hook, button measuring, placement and the flyout's work area follow the chosen taskbar
  - Fullscreen hiding only when the fullscreen app covers the widget's own monitor
- [x] Never cover taskbar icons (auto-fit)
  - Button positions via UI Automation (`Shell_TrayWnd` descendants), measured off the UI thread at startup, on display changes, after an Explorer restart and 800 ms after foreground changes
  - The widget stays in a free gap; if the offset points into icons it stops right before them; hidden only when no gap has room
  - Placement uses the last measurement synchronously, so moving the widget never waits for (or races) the query
  - UI Automation reports no buttons at all while the taskbar's context menu is open and for ~1 s while it re-lays out: an empty result counts as unknown (last layout kept, retried 5× every 500 ms); docked right, an unknown layout hides the widget instead of covering the tray
- [x] Option: dock to the right of the icons (needed for left-aligned taskbars)
  - Sits in the last free gap before the tray, right edge fixed (distance counts from the tray), grows to the left
  - Pushed into the app icons it stops right after them; on vertical taskbars: below the icons, above the tray
- [x] Settings window + JSON persistence (`%AppData%\Auxline\settings.json`, debounced atomic saves, live apply)
  - [x] Position tab: monitor, placement (left / right of the icons), distance, max width, cover size (capped to the taskbar height)
  - [x] Appearance tab: theme, font (searchable list), font sizes, text colour (colour wheel + hex), flyout corners, acrylic
  - [x] Sources tab: per-app on/off and priority
  - [x] Follows Windows light/dark switches live (widget, tray icon, flyout)
- [x] Clicking the widget while the flyout is open closes it (instead of close + reopen)
- [x] Source selection
  - Every app seen in GSMTC is listed with its Start-menu name (`shell:AppsFolder`); new ones are appended, enabled
  - Toggle each source on/off; disabled sources are never shown
  - User-defined priority order replaces the hardcoded "prefer Spotify" rule (Spotify first by default)
  - Note: filtering is per app, not per site (GSMTC reports `chrome.exe`, not "YouTube")
- [ ] Design polish pass once features are complete (collect notes here)
  - [x] Flyout styles, selectable in Appearance: Classic, Native Windows 11, Big cover, Cover-tinted, Compact, Minimalist
    - Minimalist: one line for progress or volume (speaker toggle or mouse wheel, back to progress after 3 s), system accent colour; narrower (280 vs 320), so each style sets its own width
    - Designed on a Claude Design canvas first; one XAML `UserControl` per style (`Views/FlyoutStyles/`), shared building blocks in `FlyoutResources.xaml`
    - Colours are named brushes set per light/dark by `FlyoutPalette`; this is where themes plug in
    - Native: Windows' accent colour (`SystemColors.AccentColorLight2/Dark1`); Cover-tinted: dominant colour of the album art (`CoverPalette`, contrast ≥ 3:1 tested)
    - Every style rendered in both themes by a test (`FlyoutStyleRenderTests`; `AUXLINE_RENDER_DIR` saves PNGs)
  - [ ] Themes (colour sets for widget + flyout)
- [x] Autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - Respects Task Manager's Startup toggle (`Explorer\StartupApproved\Run`, odd first byte = disabled)
- [x] Tray icon (start with Windows, debug window, exit; settings once the settings window exists)
  - Native menu (`TrackPopupMenuEx`): a WPF ContextMenu closes instantly when the icon is in the overflow flyout
- [x] Dark tray menu in dark mode (native menus are light by default)
  - Undocumented `uxtheme` ordinals #135 `SetPreferredAppMode` (force dark/light) + #136 `FlushMenuThemes`; follows the taskbar theme live; falls back to light if missing
- [x] Single-instance check (named mutex)
- [x] Runs in the background; debug window only on demand
- [x] Log file (`%LocalAppData%\Auxline\logs\auxline.log`, 1 MB + 1 backup) and global error handlers
  - UI and background-task errors are logged and the app keeps running; startup failures show a message and exit

### Phase 6 - Release
- [ ] Publish as a single file: `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`
- [ ] Measure RAM/CPU over a longer session
- [ ] Optional installer (Inno Setup / MSIX)

---

## Known challenges

- **No official taskbar embedding in Windows 11.** Deskbands were removed, so the widget is a separate topmost window placed over the taskbar. Embedding it directly into Explorer (as Windhawk mods do) would require DLL injection in C++ and is out of scope for now.
- **Z-order fights.** The taskbar can draw above the widget after it is clicked; topmost has to be re-applied.
- **Spotify timeline data** is not continuous, so the progress bar has to be interpolated.
- **Firefox-based browsers** (Firefox, Zen, LibreWolf…) report the timeline only on pause/resume and clear it on their own, so without help the progress bar appears only after the first pause of each track. Chromium-based browsers report it continuously. Fix: the optional userscript in [Extras](#extras).
- **Opera / Opera GX** likely won't get volume control: their Start menu entry points to `launcher.exe`, while audio plays from `opera.exe` (untested).
- **Taskbar position.** Windows 11 only docks the taskbar at the bottom. Top works only via an unofficial registry hack, left/right only via third-party tools (ExplorerPatcher, Windhawk). Auxline adapts (flyout below / beside; cover-only widget on vertical taskbars), but these layouts get less testing.
- **Multiple media sessions.** When a browser and Spotify both play, the Sources priority list decides; with nothing playing, Windows' current session is shown.

---

## Extras

### Live progress bar in Firefox-based browsers

[`extras/firefox-position-state.user.js`](extras/firefox-position-state.user.js) makes Firefox, Zen, LibreWolf etc. report a live timeline to Windows, so the progress bar works from the first second of a video.

1. Install a userscript manager, e.g. [Violentmonkey](https://addons.mozilla.org/firefox/addon/violentmonkey/).
2. Create a new script and paste the file's contents (or open the raw file URL to install it).
3. Restart the browser or reload media tabs.

How it works: it calls `navigator.mediaSession.setPositionState()` when media starts, pauses, seeks or changes speed, plus every 5 s while playing, because Firefox clears the system timeline on its own. It sends nothing anywhere and needs no permissions (`@grant none`). Not needed for Chromium-based browsers.

---

## Getting started

```bash
git clone <repo-url>
cd Auxline
dotnet run --project Auxline
dotnet test Auxline.slnx   # unit tests (settings, timeline, taskbar placement)
```

---

## References

- [GlobalSystemMediaTransportControlsSessionManager (Microsoft Docs)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [NAudio](https://github.com/naudio/NAudio)
- [SHAppBarMessage (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage)
- [Extended Window Styles (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
