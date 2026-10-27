# SteamMonitorTray v1.4.0 — Profile controls and version switching

## English

- Restored colors when launched from an automation environment that injects monochrome defaults; explicit Windows color preferences remain respected.
- Switching versions now closes a conflicting idle PowerShell normally and waits before proceeding. Busy commands and unsubmitted input are preserved.
- Fixed settings window activation and restoration, including hidden process startup where managed visibility differs from the actual Windows window. Added `--settings` to open Settings directly at launch.

- Fixed profile-switch flicker after configuration checks by updating the full check result in one layout update.
- Fixed the automatic-monitoring checkbox: unchecked profiles can now be started manually.
- Added **Open PowerShell window** in Settings. Restore a session or open an idle prompt without starting monitoring.
- Added profile reordering with **↑ / ↓** and drag-and-drop; saved order also controls the tray menu. Stabilized the drag cursor and insertion guide, and corrected edge scrolling for short and long lists.
- Alternate versions can share history paths. Dedicated sessions reserve their output files until PowerShell exits, preventing two managed versions from using the same output concurrently.
- Configuration checks distinguish shared-output alternates from invalid configurations. Start all prioritizes the first valid profile in saved order, skips later output conflicts, and also starts independent profiles.
- Settings migrate to format 2 with an automatic backup of format 1. Updated all six interface languages and bilingual documentation.

Download **SteamMonitorTray-1.4.0-portable.zip**, extract all files, quit the old app normally and replace all runtime files together. Keep `SteamMonitorTray.exe.config` beside the executable. The monitor and Python remain separate installations. Private configurations, credentials and history are not included.

**Rollback:** to downgrade the tray, restore the old runtime files and matching pre-upgrade settings. To switch only the monitor version, stop the current monitoring and start the alternate profile; an idle conflicting PowerShell closes automatically. History is retained.

## 简体中文

- 修复工具启动时注入黑白环境导致配色消失，仍尊重明确的 Windows 配色偏好。
- 切换版本时自动正常关闭冲突的空闲 PowerShell 并等待退出；保留正在执行的命令和未提交输入。
- 修复设置窗口激活、隐藏／最小化恢复，以及隐藏进程启动时逻辑已显示但原生窗口不可见的问题；支持 `--settings` 启动时直接进入设置。

- 修复检查配置后切换账号的闪烁：完整检查结果一次更新，避免配置区域短暂上跳。
- 修复自动监测勾选框：不勾选时仍可手动启动。
- 设置页新增 **“打开 PowerShell 窗口”**，支持还原已有会话或打开空闲提示符，不自动启动监测。
- 新增 **↑ / ↓** 和拖动排序，保存顺序同时应用于托盘菜单；稳定拖动光标与插入提示线，修正短列表误滚动及长列表边缘滚动。
- 允许备用版本共用历史路径；专用会话直到 PowerShell 退出才释放文件占用，防止两个受管理版本同时使用相同输出。
- 配置检查区分共用输出提醒与配置错误；“全部启动”优先启动排序靠前且有效的配置，跳过后续输出冲突配置，其余独立配置照常启动。
- 设置迁移到格式 2，并自动备份格式 1；同步六种界面语言和中英文说明。

下载 **SteamMonitorTray-1.4.0-portable.zip** 并完整解压。正常退出旧程序后，一起替换全部运行文件，保持 `SteamMonitorTray.exe.config` 与 EXE 位于同一目录。Python 和监测程序仍需单独安装，压缩包不包含私人配置、凭据或历史记录。

**回退：**回退托盘时恢复旧运行文件及配套升级前设置；只切换监测版本时，先停止当前监测，再启动备用配置；冲突的空闲 PowerShell 会自动正常关闭。历史记录继续保留。
