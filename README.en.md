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

Click **Save**. Profiles enabled during first-run setup start automatically. Existing profiles can be selected without adding them again. Credentials, targets and monitoring history remain in their original files.

An optional command-line import is available while the app is not running:

```powershell
.\install.ps1 -ProfileDirectory 'D:\SteamProfiles\Primary'
```

Add `-NoStartup` to import without registering Windows startup. The example path is fictional.

## Native tray menu and display scaling

Version 1.2.0 uses Windows-native popup menus, following the same Win32 approach as AKKO Link Monitor, with Per-Monitor V2 DPI awareness. Windows draws the menu font, spacing, checkmarks and disabled states. The tray icon keeps its colored status indicators; menu status is shown as text using system colors. The former menu hover hints are replaced by startup status text and operation notices.

Each opening gets a fresh snapshot, including the current Task Manager startup approval. Background monitoring updates do not rebuild an open menu or its submenus. Existing commands, profile names and six interface languages are preserved, including Arabic right-to-left layout.

High DPI features require a Windows version supporting Per-Monitor V2 (Windows 10 version 1703 or later, including Windows 11). See [verification notes](https://github.com/Jazzwind/SteamMonitorTray/blob/main/docs/VERIFICATION.md) for tested environments and outstanding manual checks.

## Tray and PowerShell windows

- Double-click the tray icon to restore existing windows.
- Start or stop all enabled profiles, or control each profile separately.
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

Both entry points start enabled profiles. Hidden sessions continue monitoring. This setting affects the next launch; windows can still be shown or hidden later. Hidden consoles are created hidden, rather than briefly displayed and then hidden.

## Language

The **Language** submenu follows **Hide window on startup**. Its default is **Automatic (system language)**. Available languages are 简体中文, English, Français, Русский, Español and العربية: the [six official UN languages](https://www.un.org/our-work/official-languages).

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

Use a separate saved monitor config for each account. Profiles may share a secrets file, but must use distinct log, CSV, state and enabled game-library snapshot files. Static Python AST inspection checks output collisions without executing the config for inspection. Save numeric Steam64 targets for straightforward path detection; vanity targets require explicit output settings as described in the Chinese guide.

Profile paths are locked while PowerShell sessions exist. Stop monitoring and enter `exit` in those sessions before reopening settings to edit profiles. Global preferences remain available.

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
.\package.ps1 -Version 1.2.1
```

Tests use local fake monitors and isolated configuration/startup metadata. They do not call Steam or Discord. Release ZIPs in `dist` use a strict runtime allowlist and include both language guides. Privacy scanning covers source, staged files, reachable Git history and release archives.

Packaging builds into its own staging directory, so it does not overwrite a running copy in `bin`. A test-only interactive preview is available after building tests: `artifacts\native-menu\bin\SteamMonitorTray.Tests.exe --preview`. Its profile is disabled and startup registration is isolated; the preview closes automatically after three minutes.

Real sign-out/sign-in startup behavior should also be checked manually. Tests never sign you out or restart your computer.

The repository homepage displays English first and expands it by default. Select **简体中文** at the top to read Chinese on the same page; select **English** to return. The code listing stays on the repository homepage. This uses native exclusive disclosure sections in modern browsers. Reloading does not preserve the selected language. If the Chinese link does not respond after a reload, click the prominent **简体中文 · 点击展开中文说明** heading directly; the entire heading is clickable. Language headings can also be clicked to expand or collapse each guide manually.

For documentation updates, edit this file as the English source, then run `python scripts\SyncReadmeEnglish.py` to update the homepage's English section. The script changes only the Chinese navigation link to an in-page target; all other English content stays identical. This standalone file is also included in the portable package; the homepage offers only in-page language links.

## License and supporting upstream

SteamMonitorTray’s independently written companion code is MIT licensed; see [LICENSE](LICENSE). The separately installed upstream project has its own [GPL-3.0 license](https://github.com/misiektoja/steam_monitor/blob/main/LICENSE).

Please consider starring [steam_monitor](https://github.com/misiektoja/steam_monitor). Its maintainer also provides [GitHub Sponsors](https://github.com/sponsors/misiektoja) and [Buy Me a Coffee](https://buymeacoffee.com/misiektoja) links. For public conversation, use the upstream [Discussions](https://github.com/misiektoja/steam_monitor/discussions) rather than opening a bug report just to say thanks.
