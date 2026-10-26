<details name="release-language" open>
<summary><strong>English</strong></summary>

## SteamMonitorTray v1.2.1

A Windows tray companion for [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor).

- Fix quitting getting stuck when a console remains in text-selection (Quick Edit) mode after being hidden or minimized.
- Release active console selections before tray start, stop and quit commands. Visible windows retain normal text selection and copying; unsubmitted PowerShell commands are preserved.
- Show **Quitting…** during shutdown. If exit remains incomplete after the configured timeout (30 seconds by default), restore the console for inspection and keep the tray available.
- Keep failed exit requests retryable and disable monitoring start actions while quitting.
- Shorten the Simplified Chinese quit label to **退出** and update both language guides.

Monitoring still stops through a real Ctrl+C event, followed by normal PowerShell exit. No process is forcibly terminated.

Download **SteamMonitorTray-1.2.1-portable.zip** and extract the entire archive. To upgrade, quit the old tray app normally and replace all runtime files together in the existing folder, keeping **SteamMonitorTray.exe.config** beside the EXE. Existing local settings, monitor configuration and history remain in place. Python and steam_monitor are installed separately; this release updates the tray companion.

Verified with isolated local monitors: selection followed by hide/minimize or quit, Ctrl+C cleanup, independent sessions, unsubmitted input, exit timeout recovery and failed-send retry handling. Full C# integration tests, 12 Python offline tests and privacy checks passed.

Thanks to [misiektoja](https://github.com/misiektoja) and the steam_monitor contributors for the monitoring engine and documentation.

</details>

<details name="release-language">
<summary><h2>简体中文 · 点击展开</h2></summary>

## SteamMonitorTray v1.2.1

为 [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor) 提供 Windows 托盘管理。

- 修复控制台隐藏或最小化后仍处于文字选择（Quick Edit）状态，导致退出一直等待的问题。
- 托盘启动、停止和退出命令执行前解除选择状态；显示窗口时仍可正常选择、复制，并保留未提交的 PowerShell 命令。
- 退出过程中显示“正在退出…”。超过配置的超时（默认 30 秒）仍未完成时，还原控制台供检查，并保留托盘。
- 退出请求发送失败后允许重试；退出期间禁用启动监测操作。
- 简体中文“退出程序”简化为“退出”，同步更新中英文说明。

监测仍通过真实 Ctrl+C 正常停止，之后正常退出 PowerShell，不强制结束进程。

下载 **SteamMonitorTray-1.2.1-portable.zip** 并完整解压。升级时先从旧版托盘正常退出，再在原文件夹一起替换运行文件，保持 **SteamMonitorTray.exe.config** 与 EXE 位于同一目录。已有本地设置、监测配置和历史继续保留。Python 和 steam_monitor 仍需单独安装；本次更新的是托盘管理器。

已使用隔离的本地测试程序验证：选中文字后隐藏、最小化或退出，Ctrl+C 清理、多实例隔离、未提交输入保护、退出超时恢复和发送失败重试。完整 C# 集成测试、12 项 Python 离线测试及隐私扫描通过。

感谢 [misiektoja](https://github.com/misiektoja) 及 steam_monitor 贡献者提供监测引擎与文档。

</details>
