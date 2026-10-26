<details name="release-language" open>
<summary><strong>English</strong></summary>

## SteamMonitorTray v1.3.0

A Windows tray companion for [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor).

- Make settings easier to read with a consistent 11-point base font, larger profile rows, DPI-aware label widths and a scrollable layout. Read-only paths keep normal contrast and can be copied.
- Fix DPI initialization happening after tray bitmap creation, which could leave text boxes, lists and drop-downs using small 96-DPI fonts on a high-DPI display.
- Reserve header rows using native control heights so enlarged drop-downs do not overlap the next field. Reduce resize layout work with cached text wrapping and fewer nested layouts; defer background path checks while dragging.
- Organize settings into **General** and **Monitoring profiles**, grouping runtime and profile paths on the profiles tab. Add shared Windows startup and language controls that synchronize with the tray immediately; show Task Manager disable warnings and explain which changes apply without Save.
- Add **Stop monitoring and edit**: gracefully stop monitors, close their dedicated PowerShell sessions and unlock the same settings window. No manual `exit` is needed.
- Offer **Save and resume monitoring** and **Save only**. Resume only previously running/starting profiles that remain enabled and valid, preserving their previous window visibility. Cancel leaves stopped monitoring stopped.
- Preserve unsubmitted input and other PowerShell commands. Busy or timed-out preparation identifies remaining sessions and offers a retry; no process is forcibly terminated.
- Show field-specific missing-file warnings and add asynchronous **Check configurations**, with an explanation, passed/failed totals and per-profile details. Existing broken profiles no longer block unrelated global settings or other valid profiles; new enabled path errors and output collisions remain blocked.
- Update all six interface languages and both README guides. Keep a single settings window and disable monitor starts while editing.

Download **SteamMonitorTray-1.3.0-portable.zip** and extract the entire archive. To upgrade, quit the old tray app normally, then replace all runtime files together in the existing folder. Keep **SteamMonitorTray.exe.config** beside the EXE. Local settings, startup registration, monitoring configuration and history remain in place. Python and steam_monitor are installed separately; this release updates the tray companion.

Tested with isolated local monitors on Windows 11 at 150% scaling: real Ctrl+C cleanup, multiple sessions, save/resume/save-only/cancel, busy input, timeout recovery, missing-file validation and six-language layouts. Other physical scaling factors, mixed-DPI displays and real sign-in startup still need manual verification. Source and portable archive privacy scans passed.

Thanks to [misiektoja](https://github.com/misiektoja) and the steam_monitor contributors for the monitoring engine and documentation.

</details>

<details name="release-language">
<summary><h2>简体中文 · 点击展开</h2></summary>

## SteamMonitorTray v1.3.0

为 [misiektoja/steam_monitor](https://github.com/misiektoja/steam_monitor) 提供 Windows 托盘管理。

- 设置页统一使用 11pt 基础字号，加大配置列表行高，按 DPI 调整标签宽度并支持内容滚动；只读路径保持正常文字颜色，可以选择、复制。
- 修正 DPI 初始化晚于托盘图标绘制初始化的问题，解决高缩放下输入框、下拉框和列表实际仍使用偏小字号的问题。
- 顶部按原生控件实际高度预留行距，避免放大后的下拉框覆盖下一行；缓存文字换行测量、减少布局嵌套，并在拖动窗口期间暂缓后台路径检查，改善缩放卡顿。
- 设置页分为 **“常规”** 和 **“监测配置”**，运行环境路径与账号路径统一放在监测配置页；新增与托盘即时同步的开机自启动及语言选项，保留任务管理器禁用提醒，并明确说明哪些更改无需保存即可生效。
- 新增 **“停止监测并编辑”**：正常停止监测、关闭专用 PowerShell 会话，并在当前页面解锁，无需手动输入 `exit`。
- 提供 **“保存并恢复监测”** 和 **“仅保存”**。只恢复此前正在运行或启动中、仍启用且有效的配置，并保留原先窗口显示状态；取消后已停止的监测保持停止。
- 保留未提交输入，不中断其他 PowerShell 命令；忙碌或超时时列出未关闭会话并允许重试，不强制结束进程。
- 文件缺失会定位到具体字段，并新增后台 **“检查配置”**，附带范围说明、通过及需处理数量、每份配置的具体问题。原有坏配置不再阻止无关全局设置保存或其他有效配置启动；新增已启用路径错误和重复输出仍会拦截。
- 同步六种界面语言和中英文 README；只保留一个设置窗口，编辑期间禁止启动监测。

下载 **SteamMonitorTray-1.3.0-portable.zip** 并完整解压。升级时先从旧版托盘正常退出，再在原文件夹一起替换运行文件，保持 **SteamMonitorTray.exe.config** 与 EXE 位于同一目录。本地设置、自启动注册、监测配置和历史继续保留。Python 和 steam_monitor 仍需单独安装；本次更新的是托盘管理器。

已在 Windows 11、150% 缩放下使用隔离的本地测试程序验证：真实 Ctrl+C 清理、多会话、保存恢复、仅保存、取消、忙碌输入、超时恢复、缺失文件检查和六语言布局。其他实际缩放比例、混合 DPI 多屏和真实登录自启动仍需人工验收。源码及便携包隐私扫描通过。

感谢 [misiektoja](https://github.com/misiektoja) 及 steam_monitor 贡献者提供监测引擎与文档。

</details>
