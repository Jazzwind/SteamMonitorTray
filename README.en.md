<div align="center">
  <h1>SteamMonitorTray</h1>
  <h3><strong>English</strong> · <a href="README.md">简体中文</a></h3>
  <p><strong>A Windows tray companion built for <a href="https://github.com/misiektoja/steam_monitor">misiektoja/steam_monitor</a>.</strong></p>
  <p>Native PowerShell · Multiple profiles · Windows startup · Six interface languages</p>
</div>

SteamMonitorTray manages real PowerShell sessions around the existing `steam_monitor` command. Hide a window without interrupting monitoring, restore the same output later, and stop gracefully from the tray.

## Upstream and acknowledgments

The monitoring engine is **[steam_monitor](https://github.com/misiektoja/steam_monitor)**, created and maintained by **[misiektoja](https://github.com/misiektoja)**. Steam activity detection, notification delivery and monitoring history are provided by that project. Thank you to misiektoja and its contributors for making this tool available and maintaining its documentation.

SteamMonitorTray adds Windows tray controls, session management, startup integration and a localized interface. It launches the separately installed upstream command; it does not bundle or automatically update the upstream source. For monitoring configuration and troubleshooting, consult the [upstream documentation](https://misiektoja.github.io/steam_monitor/).

## Requirements

- Windows 10 or 11 with .NET Framework 4.8.
- Windows PowerShell 5.1 and PSReadLine 2.0 or newer.
- Python and an installed `steam_monitor` command. Compatibility was verified with `steam_monitor 2.1`.
- An existing `steam_monitor.conf` containing a saved default target, plus a `.env` file for credentials when applicable.

This is an independent companion, not an official Valve application.

## Getting started

### Build from source

Open PowerShell in the project folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build uses the .NET Framework C# compiler supplied with Windows. No .NET SDK or NuGet download is needed. Run `bin\SteamMonitorTray.exe` after building. Close the running tray application gracefully before rebuilding its executable. To build separately while the current version keeps running, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\artifacts\preview\bin`.

For a portable release, extract the ZIP and run `SteamMonitorTray.exe` directly. Python and the upstream monitor must still be installed separately.

Keep `SteamMonitorTray.exe.config` beside the executable: it is the non-secret .NET runtime configuration that enables high DPI support, not your personal `settings.json`. When upgrading, quit the old tray app normally and replace all runtime files together. Existing settings, startup registration and monitoring data remain in their current locations.

### Add an existing monitoring profile

If the profile list is empty, click **Add existing profile…** and select the folder that already contains `steam_monitor.conf`. This is the monitor’s existing data folder, not SteamMonitorTray’s source folder. The app fills the config path, same-folder `.env` when available, and detected monitor executable.

| Field | What to select |
| --- | --- |
| Profile name | Any tray display name, such as “Main profile”. Do not enter a SteamID here. |
| Working folder | Usually the folder containing the existing monitoring config. |
| Config file (.conf) | Your existing `steam_monitor.conf`, with its saved target and notification settings. |
| Secrets file (.env) | Usually `.env` in the same folder. Leave blank only when credentials are supplied by environment variables. |
| Monitor program (.exe) | Installed `steam_monitor.exe`, not this tray app, PowerShell or Python. |
| Automatically monitor this profile when the app starts | Enable this to begin monitoring when the tray application opens. Windows startup registration is a separate option. |

Detected PowerShell and Python paths usually need no changes. If the monitor executable is not detected, run `Get-Command steam_monitor | Select-Object -ExpandProperty Path` in PowerShell and select the returned executable.

Click **Save**. Profiles selected for automatic monitoring during first-run setup start automatically. Existing profiles can be selected without adding them again. Credentials, targets and monitoring history remain in their original files.

An optional command-line import is available while the app is not running:

```powershell
.\install.ps1 -ProfileDirectory 'D:\SteamProfiles\Primary'
```

Add `-NoStartup` to import without registering Windows startup. The example path is fictional.

### View, stop and edit settings

Settings have two tabs: **General** contains window visibility, Windows startup, language and retry behavior; **Monitoring profiles** contains the PowerShell/Python runtime paths, profile paths, editing controls and configuration checks. Existing sessions and first-run profile import open the profiles tab; otherwise General opens.

**Start with Windows** and **Language** are synchronized with the tray and take effect immediately, without stopping monitoring. The startup checkbox reflects shortcut registration and shows a warning when Task Manager has disabled it. Changing the checkbox never overrides that disable. Cancel does not undo these two immediate actions; other options remain drafts until saved.

Settings use a consistent 11-point base font, larger profile rows and DPI-aware label widths. Long content scrolls inside the page while action buttons remain available. When sessions exist, profile paths are read-only but retain normal contrast and can be copied; global startup-display and retry options remain editable.

Select **Stop monitoring and edit** to pause automatic retries, send real Ctrl+C to running monitors, wait for cleanup and close their dedicated PowerShell sessions normally. The same settings window then unlocks. It is not necessary to type `exit` yourself. Monitoring start actions are disabled while editing. Opening Settings again brings the existing window forward.

If a session is busy, has unsubmitted input or does not stop before the timeout (30 seconds by default), the app keeps the page locked, identifies the remaining sessions and restores their consoles. Finish or clear the command and use **Retry entering edit mode**. Other PowerShell commands are not interrupted and no process is forcibly terminated.

| Action after stopping to edit | Result |
| --- | --- |
| Save and resume monitoring | Save, then start only profiles that were running or starting before the stop and are still present and valid. Previously stopped profiles stay stopped. |
| Save only | Save and leave monitoring stopped. |
| Cancel or close | Discard the draft and leave stopped monitoring stopped. Sessions already stopped or closed are not automatically recreated. |

The per-profile automatic-start checkbox controls monitoring when the application launches; Windows sign-in startup is a separate shared setting in General and the tray. Changing the launch-display setting does not immediately show or hide existing windows. Sessions resumed after editing preserve their previous visible or hidden state.

This page registers existing configuration paths. Monitoring features and notification options are edited in the upstream `.conf`; credentials remain in `.env`. Adding another account normally means preparing a separate upstream configuration, then adding it here. Existing paths usually require no changes unless files move or Python/steam_monitor is reinstalled. The app does not rewrite these files.

### Missing files and configuration checks

The page identifies a missing working folder, `.conf`, explicitly selected `.env` or monitor executable beside the relevant field, and marks the profile list. An empty `.env` field is allowed when using environment variables. Missing PowerShell and Python executables are reported separately. **Check configurations** checks all profiles for missing files, static configuration errors and conflicting output paths in the background, then reports passed and failed counts with per-profile details. The explanation is shown beside the action. Checks do not start monitoring, execute the `.conf` or send Steam/Discord requests or notifications.

Missing profiles remain registered for repair. Existing broken profiles do not prevent saving unrelated global options or starting other valid profiles. New or changed profile paths must pass validation before saving. Profiles that share outputs cannot both be selected for automatic monitoring; manual-only alternates are allowed.

Paths are checked again before a tray start. Lightweight existence checks during operation provide warnings without automatically stopping an existing session or replacing paths. A monitor may continue after a file disappears, depending on when the upstream program next accesses it; a running-process indicator is not a guarantee that file output or network requests are succeeding. After repairing paths, use **Check configurations** and start monitoring again when appropriate.

### Manual start, windows and profile order (v1.4.0)

The automatic-monitoring checkbox controls only startup when SteamMonitorTray opens. Unchecked profiles can still be started manually, opened in PowerShell, and included in **Start all**. Automatic retries apply only to monitoring that was actually started. **Save and resume monitoring** restores previously running profiles regardless of their automatic-start checkbox.

Settings selected from the native tray menu are explicitly brought forward; hidden or minimized settings windows are restored, including when Windows starts the GUI process hidden. Use `SteamMonitorTray.exe --settings` to open Settings directly when starting the application. Dedicated shells discard only known automation-injected monochrome defaults, while preserving Windows user/machine color preferences and the monitor’s own color setting.

Select a profile and use **Open PowerShell window** to restore its existing window or create an idle PowerShell prompt without starting monitoring. If there are draft changes, choose **Save and open** or cancel. The settings window remains open and returns to view mode. You can type `steam_monitor` in that dedicated prompt; normal status synchronization and Ctrl+C still work. Double-clicking the tray icon restores all existing independent windows; this version does not use terminal tabs.

Use **↑ / ↓** or drag a profile to reorder it while editing. The insertion line shows where it will land; pause near the top or bottom edge to scroll a longer list. Release outside the list or press Esc to cancel dragging. The selected profile and draft fields are preserved; save to persist the order used by the list, tray menu and batch operations.

Alternate versions may reference the same configuration and history files if only one is selected for automatic monitoring. **Check configurations** identifies shared output as a warning. Each dedicated PowerShell reserves its output paths until it exits. Starting or opening an alternate profile automatically closes a conflicting idle PowerShell and waits for its exit. Running monitors, other commands and unsubmitted input are never forcibly closed; busy or timed-out switches leave the new profile stopped and preserve the old session. **Start all** follows the saved profile order: it chooses the first valid profile for shared output files, skips later conflicting profiles, and starts independent profiles too. Invalid profiles are skipped without reserving their output paths. Reorder profiles to change this priority. A conflicting monitor that is already running is never interrupted automatically. This protects sessions managed by this tray app; it does not control monitor commands launched independently outside it.

Settings format 2 stores `AutoStart` instead of treating `Enabled` as a manual-start permission. Existing choices, identities and order migrate automatically, with the original file preserved as `settings.json.v1.bak` before the first write. To roll back the tray executable, also restore its matching old settings backup. A separately installed monitor can instead be switched without downgrading the tray. Back up private files outside the repository, and never run alternate versions against the same outputs simultaneously.

## Native tray menu and display scaling

Version 1.2.0 uses Windows-native popup menus, following the same Win32 approach as AKKO Link Monitor, with Per-Monitor V2 DPI awareness. Windows draws the menu font, spacing, checkmarks and disabled states. The tray icon keeps its colored status indicators; menu status is shown as text using system colors. The former menu hover hints are replaced by startup status text and operation notices.

Each opening gets a fresh snapshot, including the current Task Manager startup approval. Background monitoring updates do not rebuild an open menu or its submenus. Existing commands, profile names and six interface languages are preserved, including Arabic right-to-left layout.

High DPI features require a Windows version supporting Per-Monitor V2 (Windows 10 version 1703 or later, including Windows 11). See [verification notes](https://github.com/Jazzwind/SteamMonitorTray/blob/main/docs/VERIFICATION.md) for tested environments and outstanding manual checks.

## Tray and PowerShell windows

- Double-click the tray icon to restore existing windows.
- Start or stop profiles together, or control each profile separately.
- Minimize a console to hide it in the tray. Its output and monitoring session are preserved.
- Hiding or minimizing a console releases any active text selection so Quick Edit cannot pause a hidden session. Visible windows still support selecting and copying text; tray commands also release a selection before proceeding.
- **Quit** automatically requests graceful shutdown of every monitor and then exits PowerShell. You do not need to press Ctrl+C manually before quitting.
- The console close button is disabled during monitoring to prevent Windows from closing the entire console directly.
- If shutdown takes more than 30 seconds, the app waits and offers another stop request. It never automatically force-kills a monitor.
- While quitting, the tray shows **Quitting…**. If exit remains incomplete after 30 seconds, the console is restored for inspection and the tray stays available. Finish or clear any pending command and try **Quit** again.

After monitoring stops, the real PowerShell prompt remains available. Enter ordinary commands or type `steam_monitor` to start again. Keyboard Ctrl+C also stops the monitor normally. The wrapper function is limited to app-owned sessions; your PowerShell profile is not changed.

Tray start requests preserve unsubmitted input and reject a busy prompt. The console uses Windows Console Host rather than a Windows Terminal tab. Launching the monitor by a full executable path or creating your own background jobs bypasses tray tracking; use the tray or the `steam_monitor` function for managed monitoring.

## Hide window on startup

The tray menu and settings window share these options:

| Option | Manual launch | Windows startup |
| --- | --- | --- |
| Never hide | Show window | Show window |
| Only when starting with Windows (default) | Show window | Tray only |
| Hide on every launch | Tray only | Tray only |

Both entry points start profiles selected for automatic monitoring. Hidden sessions continue monitoring. This setting affects the next launch; windows can still be shown or hidden later. Hidden consoles are created hidden, rather than briefly displayed and then hidden.

## Language

The **Language** submenu follows **Hide window on startup**; the same selector is available in **Settings → General**. Its default is **Automatic (system language)**. Available languages are 简体中文, English, Français, Русский, Español and العربية: the [six official UN languages](https://www.un.org/our-work/official-languages).

Automatic selection follows the ordered Windows UI language preferences, not regional number/date formatting. Chinese variants use Simplified Chinese; unsupported language lists fall back to English. Manual selection is saved locally and immediately updates tray controls and open settings windows. Arabic uses right-to-left layout while paths remain left-to-right.

User-provided profile names and paths remain unchanged. Upstream monitoring output and Windows file dialogs retain their own language behavior. A new app-owned PowerShell session uses the selected language for the companion’s introductory banner.

## Windows startup

Check **Start with Windows** to create the current user’s `SteamMonitorTray.lnk` startup shortcut. Uncheck it to remove that shortcut. It launches the EXE directly with `--startup`; no batch or PowerShell launcher is used.

| Menu state | Meaning |
| --- | --- |
| Unchecked, not registered | Click to register Windows startup. |
| Checked | Registered; no Task Manager disable detected. |
| Checked, disabled in Task Manager | Registered, but blocked by Windows. Enable it manually in Task Manager → Startup apps. |
| Checked, state unknown/unavailable | Registered, but Windows approval cannot be confirmed. Check Task Manager. |

The checkmark records registration, not permission to override Task Manager. Re-registering never clears a Task Manager disable. Each menu opening reads current state. Unknown `StartupApproved` formats are reported as unknown instead of guessed. Move the app by unchecking and checking startup again to refresh its path.

Windows startup here means after the current user signs in, not a service running before sign-in. `install.ps1` also registers startup; `uninstall.ps1` removes only this app’s shortcut and preserves local settings, profiles and monitoring history.

The settings section is ordered **Settings and profiles → Open local settings folder → Start with Windows → Hide window on startup → Language**.

## Multiple profiles and recovery

Use a separate saved monitor config for each account. Profiles monitoring different accounts may share a secrets file, but must use distinct log, CSV, state and enabled game-library snapshot files. Alternate-version profiles for the same account may share outputs under the session reservation rules above. Static Python AST inspection checks output collisions without executing the config for inspection. Save numeric Steam64 targets for straightforward path detection; vanity targets require explicit output settings as described in the Chinese guide.

Profile paths are locked while PowerShell sessions exist. Use **Stop monitoring and edit** to close them normally and unlock the same settings window. General preferences remain available.

Unexpected nonzero exits retry after 5, 15 and 30 seconds, then stop. Ten minutes of stable operation resets the retry budget. A manual stop, keyboard Ctrl+C, normal zero exit or Quit cancels retries. Upstream-managed network recovery does not create duplicate processes. “Monitor process running” does not certify Steam API or notification delivery health.

## Local data and privacy

Settings are stored at `%LOCALAPPDATA%\SteamMonitorTray\settings.json`, independently of the repository. Session descriptors are in its private `sessions` subfolder and are removed on normal exit. Named pipes are restricted to the current user and authenticate each session.

The size-limited `launcher.log` records random profile identifiers, states and error types. It does not capture monitor output, credentials or targets. Keep `.env`, real `.conf` files, logs, CSV, state JSON and sensitive screenshots outside Git. Language resources are embedded in the application and require no translation service or network access.

## Tests and releases

Run native integration tests from an ordinary interactive Windows desktop, not a console/pipe-restricted sandbox:

```powershell
.\build.ps1 -Test -OutputDirectory .\artifacts\native-menu\bin
python -m unittest discover -s tests -p 'test_*.py'
python .\scripts\ScanPrivacy.py
.\package.ps1 -Version 1.4.0
```

Tests use local fake monitors and isolated configuration/startup metadata. They do not call Steam or Discord. Release ZIPs in `dist` use a strict runtime allowlist and include both language guides. Privacy scanning covers source, staged files, reachable Git history and release archives.

Font regression tests also start a fresh process in the real tray initialization order and inspect the native fonts used by text boxes, lists and drop-downs. Checking managed font properties or bitmap previews alone can miss startup DPI errors.

Resize regressions check native header row spacing, fixed footer bounds and pause/resume of background path checks during an interactive resize loop. A local timing probe reports repeated width-change costs; those timings are not a guarantee of drag frame rates on every system.

Packaging builds into its own staging directory, so it does not overwrite a running copy in `bin`. A test-only interactive preview is available after building tests: `artifacts\native-menu\bin\SteamMonitorTray.Tests.exe --preview`. Its profile does not start automatically and startup registration is isolated; the preview closes automatically after three minutes.

Real sign-out/sign-in startup behavior should also be checked manually. Tests never sign you out or restart your computer.

The repository homepage displays English first and expands it by default. Select **简体中文** at the top to read Chinese on the same page; select **English** to return. The code listing stays on the repository homepage. This uses native exclusive disclosure sections in modern browsers. Reloading does not preserve the selected language. If the Chinese link does not respond after a reload, click the prominent **简体中文 · 点击展开中文说明** heading directly; the entire heading is clickable. Language headings can also be clicked to expand or collapse each guide manually.

For documentation updates, edit this file as the English source, then run `python scripts\SyncReadmeEnglish.py` to update the homepage's English section. The script changes only the Chinese navigation link to an in-page target; all other English content stays identical. This standalone file is also included in the portable package; the homepage offers only in-page language links.

## License and supporting upstream

SteamMonitorTray’s independently written companion code is MIT licensed; see [LICENSE](LICENSE). The separately installed upstream project has its own [GPL-3.0 license](https://github.com/misiektoja/steam_monitor/blob/main/LICENSE).

Please consider starring [steam_monitor](https://github.com/misiektoja/steam_monitor). Its maintainer also provides [GitHub Sponsors](https://github.com/sponsors/misiektoja) and [Buy Me a Coffee](https://buymeacoffee.com/misiektoja) links. For public conversation, use the upstream [Discussions](https://github.com/misiektoja/steam_monitor/discussions) rather than opening a bug report just to say thanks.
