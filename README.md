<details name="readme-language" open>
<summary><strong>简体中文</strong></summary>

<a name="chinese"></a>
<a name="readme-top"></a>
<div align="center">
  <h1>SteamMonitorTray</h1>
  <p><strong>基于 <a href="https://github.com/misiektoja/steam_monitor">misiektoja/steam_monitor</a> 的 Windows 托盘管理器</strong></p>
  <p>原生 PowerShell · 多账号配置 · 开机自启动 · 六种界面语言</p>
  <p><strong>简体中文</strong> · <a href="#user-content-english">English</a></p>
</div>

为 [steam_monitor](https://github.com/misiektoja/steam_monitor) 提供 Windows 托盘、原生 PowerShell 窗口、多配置管理和登录自启动。

每份配置拥有独立的 PowerShell 会话。收进托盘后监测继续，恢复窗口仍是原来的输出现场。停止使用真正的 Ctrl+C，停止后回到 PowerShell 提示符。

点击 **“退出程序”** 会自动正常停止监测并退出，无需先手动按 Ctrl+C。

## 上游项目与致谢

本项目的核心监测能力来自 **[steam_monitor](https://github.com/misiektoja/steam_monitor)**，由 **[misiektoja](https://github.com/misiektoja)** 创建并维护。Steam 活动识别、通知发送和监测历史等能力由上游提供。感谢 misiektoja 及贡献者的开发、维护和文档工作。

SteamMonitorTray 在此基础上提供 Windows 托盘控制、会话管理、自启动和多语言界面，通过已安装的命令调用上游程序。监测配置和故障排查请参阅 [上游官方文档](https://misiektoja.github.io/steam_monitor/)。欢迎为 [上游仓库](https://github.com/misiektoja/steam_monitor) 点 Star；作者也提供了 [GitHub Sponsors](https://github.com/sponsors/misiektoja) 和 [Buy Me a Coffee](https://buymeacoffee.com/misiektoja) 支持渠道。

## 环境要求

- Windows 10 / 11，.NET Framework 4.8。
- Windows PowerShell 5.1 与 PSReadLine 2.0 或以上。
- 已安装 Python 和 `steam_monitor`；当前兼容验证基于 `steam_monitor 2.1`。
- 为每个账号准备一份保存了默认目标的 `steam_monitor.conf`，凭据放在对应 `.env` 中。

本项目独立调用上游程序，不附带、修改或自动升级它。上游代码遵循其 GPL-3.0 许可，本项目自行编写的包装器采用 MIT 许可。不是 Valve 官方产品。

## 快速开始

### 从源码构建

在项目目录打开 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用系统自带的 .NET Framework C# 编译器，无需安装 .NET SDK 或下载 NuGet 依赖。程序位于 `bin\SteamMonitorTray.exe`。

### 选择已有配置

双击程序，首次启动时打开设置。如果左侧列表为空，先点击 **“添加已有配置…”**，选择你原来保存 `steam_monitor.conf` 的文件夹，不是托盘程序源码或 EXE 所在的文件夹。程序会自动填写配置、同目录的 `.env` 和已安装的 `steam_monitor.exe`。

选择左侧刚添加的配置，按下表核对。顶部 PowerShell 和 Python 如果已识别正确，通常无需修改。

| 设置项 | 含义和填写方式 |
| --- | --- |
| 配置名称 | 托盘中显示的名称，例如“主要账号”。随便命名，不用填写 SteamID。 |
| 工作目录 | 监测运行时使用的文件夹。通常是原来保存 `steam_monitor.conf` 的目录。 |
| 配置文件（.conf） | 选择原有 `steam_monitor.conf`，其中保存默认监测目标、通知开关等设置。 |
| 密钥文件（.env） | 通常选择同目录的 `.env`，其中保存 Steam API key、Discord webhook 等密钥。已有这个文件时请选中它；只有已另行设置环境变量时才留空。 |
| 监测程序（exe） | 已安装的 `steam_monitor.exe`。不要选 `SteamMonitorTray.exe`、PowerShell 或 Python。通常会自动找到。 |
| 打开托盘程序后，自动开始监测此账号 | 建议勾选。表示每次打开托盘程序时自动监测这份配置；登录 Windows 时是否打开程序，由自启动项另行控制。 |

核对后点击 **保存**。首次配置保存后会启动已勾选的监测；以后打开程序也会自动启动这些配置。已有配置时，从左侧选中后即可查看，不需要重复添加。

如果没有自动找到监测程序，在 PowerShell 中执行 `Get-Command steam_monitor | Select-Object -ExpandProperty Path`，把显示的完整程序路径填入“监测程序（exe）”。API key、webhook 和 SteamID 不需要直接填写在这个设置窗口中。

也可以在程序尚未运行时导入目录并注册自启动：

```powershell
.\install.ps1 -ProfileDirectory 'D:\SteamProfiles\Primary'
```

已有目录中的 `.env`、配置和历史不会被复制到仓库。上述路径只是示例；不要把真实配置或凭据粘贴进项目源码。

只导入配置，暂时不注册自启动：

```powershell
.\install.ps1 -ProfileDirectory 'D:\SteamProfiles\Primary' -NoStartup
```

从便携发布包使用时，程序就在解压目录，直接运行 `SteamMonitorTray.exe`；无需构建。

## 托盘和窗口

- 双击托盘图标：显示已有窗口。
- 每份配置提供显示、隐藏、启动、停止、打开日志文件夹。
- 顶部提供全部启动、全部停止；配置名称由用户自己命名。
- 最小化控制台会收进托盘。监测期间禁用右上角关闭按钮，避免 Windows 直接关闭整个控制台。
- “退出程序”先向全部监测实例发送 Ctrl+C，待监测退出后再正常退出 PowerShell。
- 如果 30 秒内还没有停止，状态显示“仍在等待停止”；可以继续等或再次发送 Ctrl+C，程序不会自动强杀。

停止之后可以输入普通 PowerShell 命令，也可以直接输入 `steam_monitor` 再次启动。包装函数只存在于本工具创建的会话里，不改动你的 PowerShell 用户配置。

托盘启动请求通过当前用户专用命名管道和会话内 PSReadLine F24 绑定进入空闲提示符，不使用抢焦点的 SendKeys。已有未提交命令时拒绝插入并提示忙碌，输入保持原样。

手动使用**完整可执行文件路径**绕过 `steam_monitor` 包装函数，或在该终端自行启动后台任务，不属于托盘可管理的监测调用。请通过托盘或直接输入 `steam_monitor` 使用管理功能。交互式设置、帮助、doctor 等一次性命令不作为监测实例计数。

控制台采用 Windows Console Host 的原生窗口，而不是 Windows Terminal 标签页。不会修改系统默认终端。

## 启动时隐藏窗口

托盘菜单和设置界面中的 **“启动时隐藏窗口”** 使用相同的三个选项：

| 选项 | 手动启动 | 开机自启动（登录 Windows 后） |
| --- | --- | --- |
| 不隐藏 | 显示窗口 | 显示窗口 |
| 仅开机自启动时隐藏（默认） | 显示窗口 | 留在托盘 |
| 每次启动时隐藏 | 留在托盘 | 留在托盘 |

两种入口都自动启动已启用的配置。“隐藏”表示启动时只显示托盘图标，监测照常运行。修改选项影响下次启动；之后仍可从托盘显示或隐藏窗口。已有设置会保留，只更新界面名称。

程序本体为无控制台的 Windows 可执行文件，会话控制台从创建时就隐藏，需要时才显示。不是先弹出再隐藏。自动化测试会监听窗口显示事件，检查托盘启动时没有初始显示事件；真实登录启动还应按文末清单实机检查。

## 语言

托盘 **“启动时隐藏窗口”** 下方新增 **“语言”** 菜单，默认选中 **“自动（跟随系统）”**。提供简体中文、English、Français、Русский、Español、العربية，覆盖联合国目前的 [六种官方语言](https://www.un.org/our-work/official-languages)。

自动选择按照 Windows 的界面语言偏好顺序匹配，不使用地区的数字或日期格式；中文地区变体使用简体中文，没有匹配语言时使用英语。手动选择会保存到本地，并立即更新托盘和已打开的设置窗口。阿拉伯语采用从右向左布局，文件路径仍从左向右显示。

用户填写的配置名称和路径保持原样。原版 steam_monitor 的监测输出、Windows 文件选择对话框有各自的语言行为；新建会话中的本程序引导提示使用所选语言。翻译资源直接内置，不调用在线翻译服务。

GitHub 首页默认展开中文。点击顶部 **English**，会在当前页面显示英文并收起中文；点击 **简体中文** 可以切回，仓库上方的代码列表保持不变。浏览器需支持原生互斥折叠；较旧浏览器可通过语言标题旁的三角箭头手动展开或收起。

## 登录自启动与任务管理器

托盘中有一个可以勾选的 **“开机自启动”** 选项。勾选后，在**当前用户**启动文件夹创建 `SteamMonitorTray.lnk`，直接指向 EXE 并传入 `--startup`；取消勾选则移除这个快捷方式。也可以运行 `install.ps1` 注册。

| 菜单显示 | 含义 |
| --- | --- |
| 未勾选：开机自启动（未注册） | 没有注册启动项，点击即可注册。 |
| 已勾选：开机自启动 | 启动项已注册，未检测到任务管理器禁用。 |
| 已勾选：开机自启动（任务管理器已禁用） | 仍然保留启动项，但 Windows 已禁止它自启动；需要到任务管理器手动启用。 |
| 已勾选：开机自启动（系统状态未知／状态读取失败） | 启动项已注册，但无法可靠确认 Windows 的批准状态，请在任务管理器核对。 |

勾选代表“已经注册、希望自启动”，不能覆盖任务管理器的禁用决定。每次打开托盘菜单都会重新读取实际状态，因此在任务管理器中修改后，无需重启本程序。

菜单后半部分按以下顺序排列：**设置与多配置管理 → 打开本地设置目录 → 开机自启动 → 启动时隐藏窗口 → 语言**。

打开任务管理器 → **启动应用**，找到 **SteamMonitorTray**，即可启用或禁用。也可以按 `Win+R`，输入 `shell:startup` 查看快捷方式。这里的开机启动指登录 Windows 后启动，不是登录前的系统服务。

安装操作和菜单勾选都不修改 Windows 的 `StartupApproved` 禁用记录；你在任务管理器中禁用后，取消再重新勾选也不会擅自重新启用。移动仓库后，可取消再勾选来更新快捷方式路径。

禁用提醒只读检查本程序对应的当前用户 `StartupApproved\StartupFolder` 记录。这是 Windows 兼容性状态，不是稳定的公开 API；遇到无法识别的格式或状态码时显示未知，不猜测为已启用。

移除自启动：

```powershell
.\uninstall.ps1
```

卸载脚本只删除本项目的启动快捷方式，不终止监测，不删除配置、历史或项目目录。请从托盘正常退出。

## 多账号

上游一次调用只监测一个默认目标。为另一个账号准备**另一份配置**，在设置中添加并命名，即可分别或全部启停。不同配置可以引用同一份 `.env`，不必复制凭据。

每份配置的日志、CSV、状态文件和启用游戏库检查时的快照必须分开。启动前会通过 Python AST 静态检查文件路径，阻止重复输出。不会执行配置来做检查，也不会把检查结果写入包装器日志。

默认目标建议保存为数字 Steam64 ID。使用非数字目标时，需要明确设置 `FILE_SUFFIX` 和 `STEAM_STATUS_FILE`，游戏库快照检查则需要数字目标，避免错误猜测上游解析后的文件名。

设置窗口只管理文件引用，不编辑 API key、webhook 或目标。新增目标仍使用上游 `steam_monitor --setup`；生成的私人文件应保存在仓库以外。

配置会话存在时，配置字段暂不可修改。先停止监测，在对应 PowerShell 提示符输入 `exit` 关闭窗口，再打开设置。全局启动策略与恢复开关仍可修改。

## 状态与恢复

状态包括正在启动、监测进程运行、正在停止、已停止、等待重试、异常。**进程运行不代表 Steam API、Discord 或网络始终健康**，具体情况以原程序输出为准。

监测非零异常退出后默认在 5、15、30 秒后各重试一次，三次重试仍失败则停止；持续运行十分钟重置重试预算。手动停止、键盘 Ctrl+C、零退出码正常结束及主动退出会取消重试。上游自身处理的断网不会触发包装器重启。

托盘正常退出时，如果提示符中已有未提交输入，会拒绝覆盖并取消退出；请完成或清空输入后重试。如果正在执行其他 PowerShell 命令，则等待它回到提示符。

## 本地文件与隐私

本地设置：`%LOCALAPPDATA%\SteamMonitorTray\settings.json`。

本地会话描述：同目录 `sessions` 下的临时文件，正常退出后清理。目录权限限制为当前用户和 SYSTEM。命名管道限制为当前用户，并校验每个会话生成的随机令牌。

本地包装器日志：同目录 `launcher.log`，限制大小，仅记录随机配置标识、状态和错误类型。不会收集原监测输出全文、API key、webhook、SteamID 或配置内容。

以下内容不应进入 GitHub：

- `.env`、真实 `steam_monitor.conf`、本地 `settings.json`。
- 监测日志、CSV、状态 JSON、游戏库快照、设置过程记录。
- 含目标账号、密钥、webhook 或个人目录的截图。

`.gitignore` 已排除运行目录及常见私人文件。发布包使用明确的文件白名单，不会打包整个 `bin` 或运行目录。扫描同时覆盖源文件、暂存区、可达提交历史及发布 ZIP；这不能替代人工审阅。

## 测试和打包

在普通交互式 Windows 会话中运行，不要在限制控制台/命名管道的沙箱内执行集成测试：

```powershell
.\build.ps1 -Test
python .\scripts\ScanPrivacy.py
.\package.ps1
```

测试只运行本地 FakeMonitor，不使用真实配置，不连接 Steam 或 Discord。覆盖隐藏启动、真实 Ctrl+C 清理、实例隔离、输入保留、原生提示符、手动启停同步、输出冲突、管理器重试上限和正常退出。测试产物位于已忽略的 `artifacts` 中。

发布 ZIP 位于 `dist`。这是便携运行包，Python 和 `steam_monitor` 需由使用者自行安装。构建脚本兼容 Windows PowerShell 5.1；复制到发布目录的会话脚本使用 UTF-8 BOM，避免中文乱码。

维护英文文档时，以 `README.en.md` 为英文正文来源，修改后运行 `python scripts\SyncReadmeEnglish.py` 同步首页英文区。同步时仅将“简体中文”链接改为页内切换，其余英文内容保持一致。`README.en.md` 同时作为便携包中的独立英文说明；首页仅提供页内语言切换入口。

## 真实机器验收

- 三种显示模式分别手动测试；托盘模式不能闪黑框或出现任务栏窗口。
- 最小化、显示、隐藏均保持原有输出和监测运行。
- 任务管理器禁用启动项后重新登录不启动，重新启用后按策略启动。
- 登录初期网络不可用时检查原监测输出，不要把“进程运行”当成网络健康。
- 确认原来的日志、CSV 和状态文件继续写入原目录。

自动测试不替代真实注销/登录检查；程序不会为了验证自启动而自动注销或重启你的电脑。

</details>

<details name="readme-language">
<summary><strong>English</strong></summary>

<a name="english"></a>

<!-- ENGLISH_GUIDE_START -->
<div align="center">
  <h1>SteamMonitorTray</h1>
  <p><strong>A Windows tray companion built for <a href="https://github.com/misiektoja/steam_monitor">misiektoja/steam_monitor</a>.</strong></p>
  <p>Native PowerShell · Multiple profiles · Windows startup · Six interface languages</p>
  <p><a href="#user-content-chinese">简体中文</a> · <strong>English</strong> · <a href="https://github.com/misiektoja/steam_monitor">Upstream project</a></p>
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

The build uses the .NET Framework C# compiler supplied with Windows. No .NET SDK or NuGet download is needed. Run `bin\SteamMonitorTray.exe` after building. Close the running tray application gracefully before rebuilding its executable.

For a portable release, extract the ZIP and run `SteamMonitorTray.exe` directly. Python and the upstream monitor must still be installed separately.

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

## Tray and PowerShell windows

- Double-click the tray icon to restore existing windows.
- Start or stop all enabled profiles, or control each profile separately.
- Minimize a console to hide it in the tray. Its output and monitoring session are preserved.
- **Quit** automatically requests graceful shutdown of every monitor and then exits PowerShell. You do not need to press Ctrl+C manually before quitting.
- The console close button is disabled during monitoring to prevent Windows from closing the entire console directly.
- If shutdown takes more than 30 seconds, the app waits and offers another stop request. It never automatically force-kills a monitor.

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
.\build.ps1 -Test
python -m unittest discover -s tests -p 'test_*.py'
python .\scripts\ScanPrivacy.py
.\package.ps1 -Version 1.1.0
```

Tests use local fake monitors and isolated configuration/startup metadata. They do not call Steam or Discord. Release ZIPs in `dist` use a strict runtime allowlist and include both language guides. Privacy scanning covers source, staged files, reachable Git history and release archives.

Real sign-out/sign-in startup behavior should also be checked manually. Tests never sign you out or restart your computer.

The repository homepage opens in Chinese by default. Select **English** to show this guide on the same page and collapse Chinese; select **简体中文** to switch back. The code listing stays on the repository homepage. This uses native exclusive disclosure sections in modern browsers. Older browsers can expand or collapse each language manually using its disclosure arrow.

For documentation updates, edit this file as the English source, then run `python scripts\SyncReadmeEnglish.py` to update the homepage's English section. The script changes only the Chinese navigation link to an in-page target; all other English content stays identical. This standalone file is also included in the portable package; the homepage offers only in-page language links.

## License and supporting upstream

SteamMonitorTray’s independently written companion code is MIT licensed; see [LICENSE](LICENSE). The separately installed upstream project has its own [GPL-3.0 license](https://github.com/misiektoja/steam_monitor/blob/main/LICENSE).

Please consider starring [steam_monitor](https://github.com/misiektoja/steam_monitor). Its maintainer also provides [GitHub Sponsors](https://github.com/sponsors/misiektoja) and [Buy Me a Coffee](https://buymeacoffee.com/misiektoja) links. For public conversation, use the upstream [Discussions](https://github.com/misiektoja/steam_monitor/discussions) rather than opening a bug report just to say thanks.
<!-- ENGLISH_GUIDE_END -->

</details>
