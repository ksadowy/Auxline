# Auxline

A lightweight, customizable media widget for the Windows 11 taskbar. It shows the current track (cover art, title, artist) in the empty area on the left side of the taskbar. Clicking it opens a flyout with playback controls and volume.

Works with Spotify and any other app that reports to Windows media controls (browsers, YouTube, etc.). No Spotify API keys required.

---

## Features (planned)

- [ ] Compact widget on the taskbar: cover thumbnail, title, artist
- [ ] Flyout on click: large cover art, play/pause, next/previous, volume slider
- [ ] Per-app volume control (changes Spotify volume only, not system volume)
- [ ] Progress bar / seek (if the player reports timeline)
- [ ] Source selection: choose which apps (Spotify, browsers, etc.) the widget connects to, and their priority
- [ ] Customization: position, size, colors, fonts, acrylic/blur, corner radius
- [ ] Auto-hide when a fullscreen app or game is running
- [ ] Multi-monitor and DPI-aware
- [ ] Start with Windows
- [ ] Tray icon with settings and exit

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
├── Models/
│   └── MediaTrack.cs             # Immutable snapshot of the current track
├── ViewModels/
│   └── PlayerViewModel.cs        # Now-playing state + commands, shared by all windows
├── Services/
│   ├── MediaService.cs           # GSMTC: current session, track info, controls
│   ├── VolumeService.cs          # Core Audio: per-app volume
│   ├── AppIdentity.cs            # Maps GSMTC app ID → process name
│   ├── TaskbarService.cs         # Taskbar position, fullscreen detection
│   └── SettingsService.cs        # Load/save JSON settings
├── Views/
│   ├── DebugWindow.xaml          # Phase 1: track info + controls, for testing
│   ├── WidgetWindow.xaml         # Compact widget sitting on the taskbar
│   ├── FlyoutWindow.xaml         # Popup with cover art and controls
│   └── SettingsWindow.xaml       # Customization UI
├── Interop/
│   └── NativeMethods.cs          # P/Invoke declarations
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
- [ ] Hide when a fullscreen app is running (`SHQueryUserNotificationState`, or compare the foreground window bounds with the monitor bounds)
- [x] Hide when nothing is playing (content collapses, window shrinks to zero width)
- [ ] Handle taskbar auto-hide
- [ ] Multi-monitor support (choose which taskbar)
- [ ] Settings window + JSON persistence
- [ ] Source selection
  - List every source seen so far (`SourceAppUserModelId`, e.g. `Spotify.exe`, `chrome.exe`)
  - Toggle each source on/off; disabled sources are skipped when picking the session
  - User-defined priority order replaces the hardcoded "prefer Spotify" rule
  - Note: filtering is per app, not per site (GSMTC reports `chrome.exe`, not "YouTube")
- [x] Autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - Respects Task Manager's Startup toggle (`Explorer\StartupApproved\Run`, odd first byte = disabled)
- [x] Tray icon (start with Windows, debug window, exit; settings once the settings window exists)
  - Native menu (`TrackPopupMenuEx`): a WPF ContextMenu closes instantly when the icon is in the overflow flyout
- [ ] Dark tray menu in dark mode (native menus are light by default)
- [x] Single-instance check (named mutex)
- [x] Runs in the background; debug window only on demand

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
- **Multiple media sessions.** When a browser and Spotify both play, decide which one to show (prefer Spotify, or the most recently active one).

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
```

---

## References

- [GlobalSystemMediaTransportControlsSessionManager (Microsoft Docs)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [NAudio](https://github.com/naudio/NAudio)
- [SHAppBarMessage (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage)
- [Extended Window Styles (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
