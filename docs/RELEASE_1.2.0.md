<details name="release-language" open>
<summary><strong>English</strong></summary>

## SteamMonitorTray v1.2.0

A Windows tray companion for [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor).

- Replace WinForms popup menus with Windows-native menus, using the same Win32 approach as AKKO Link Monitor.
- Enable Per-Monitor V2 DPI support. Menu fonts, checkmarks and spacing follow Windows settings; colored status indicators remain in the tray icon.
- Keep open menus and submenus stable while monitoring status changes in the background.
- Preserve profile management, startup registration and approval warnings, six interface languages, and graceful Ctrl+C shutdown.
- Improve settings layout at high DPI, including longer translated Save/Cancel labels.
- Add separate build output and isolated release staging so a build need not overwrite a running copy.

Download **SteamMonitorTray-1.2.0-portable.zip**, extract the entire archive and run **SteamMonitorTray.exe**. Keep **SteamMonitorTray.exe.config** beside it. Python and steam_monitor must be installed and configured separately.

To upgrade, quit the old tray app normally, then replace all runtime files together. Existing private settings, monitor configuration and history remain in place. This version was tested on Windows 11 at 150% scaling with local fake monitors; other physical scale factors, mixed-DPI displays and real sign-in startup still need manual verification.

Thanks to [misiektoja](https://github.com/misiektoja) and the steam_monitor contributors for the monitoring engine and documentation.

</details>

<details name="release-language">
<summary><h2>简体中文 · 点击展开</h2></summary>

## SteamMonitorTray v1.2.0

为 [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor) 提供 Windows 托盘管理。

- 参考 AKKO Link Monitor 的 Win32 机制，将 WinForms 右键菜单替换为 Windows 原生菜单。
- 启用 Per-Monitor V2 DPI 感知；字体、勾选和间距由系统绘制，托盘图标继续保留颜色状态提示。
- 后台监测状态变化时，已展开的主菜单与子菜单保持稳定。
- 保留多配置管理、自启动注册及禁用提醒、六种语言和真实 Ctrl+C 正常停止。
- 改善高 DPI 设置布局，包括较长翻译文本的保存、取消按钮。
- 支持独立构建目录和独立打包暂存，不必覆盖正在运行的版本。

下载 **SteamMonitorTray-1.2.0-portable.zip**，完整解压后运行 **SteamMonitorTray.exe**。请将 **SteamMonitorTray.exe.config** 与 EXE 放在一起。Python 和 steam_monitor 仍需自行安装并完成配置。

升级时先从旧版托盘正常退出，再一起替换运行文件。私人设置、监测配置和历史继续保留在原位置。本版本在 Windows 11、150% 缩放下使用本地假监测程序完成测试；其他实际缩放比例、混合 DPI 多屏及真实登录启动仍需人工验收。

感谢 [misiektoja](https://github.com/misiektoja) 及 steam_monitor 贡献者提供监测引擎与文档。

</details>
