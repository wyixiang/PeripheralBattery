# 独立启动与日志迁移（1.3.5）

## 本次已核实证据

电量工具 1.3.4、PID 58776 的最后一次会话在 2026-11-13 10:21:26+08:00 收到 `windows-session-ending: Shutdown`，随后记录 WPF Exit、清理完成和 `exited / 0`。同一时间窗的 System 事件 43 开始安装 OpenAI.Codex 更新，AppModel-Runtime 事件 217 随后销毁旧 Codex 桌面容器。

其真实文件位于 `%LOCALAPPDATA%\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\PeripheralBattery`。这些独立的外设工具记录表明它继承了包环境，并在这次更新关联的会话结束中正常退出，没有对应的致命崩溃记录。它们不用于推定 11 月 10 日那次缺少记录的退出原因。

旧快照另含较早的未观察任务异常，代码栈为首次刷新后从后台线程调用 WPF 重绘。它不是此次正常退出的原因；1.3.5 把首次异步刷新排到 WPF Dispatcher，并拒绝直接在后台线程修改界面。

本机原始会话、日志及关联系统事件在修改运行文件前复制到 `artifacts/investigation-20261113`，不进入 Git。

## 启动方式

在取得单实例锁前查询 `GetPackageFullName`、`IsProcessInJob` 和当前 Job 限制；普通无包、无 Job 的桌面进程直接启动。发现宿主绑定时，以 `CreateProcessW` 创建挂起子进程，使用允许的显式 Job breakaway 和 Desktop Bridge 的进程树脱离策略，核验子进程无包身份且不属于任何 Job 后才恢复执行。`--standalone` 防止递归重启，辅助退出命令不写会话。

未满足独立条件时终止的只是本工具刚创建、尚未执行的挂起子进程，启动失败会留记录；不改宿主 Job、不关闭 Codex、不加守护进程、不修改默认开机启动。由于多层 Job 的限制可能更严格，不能用“允许创建子进程”代替实际独立状态检查。

参考微软文档：[Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)、[UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)、[Known Folder Flags](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ne-shlobj_core-known_folder_flag)。

## 数据与回归

- 明确使用非包重定向的 LocalAppData；从已确认的旧 Codex 缓存导入已知外设记录。逐文件只复制缺失项，检查 JSON，并保留原始来源和已有目标。
- 启动日志写入包名、Job 状态、限制位，以及通过实际文件句柄确认的物理目录，不记录任意环境变量或命令行载荷。
- `tests/startup/StartupChecks.csproj` 的 22 项检查使用两个自建、关闭即终止成员的宿主 Job（`0x2800` 与 `0x3000`）。宿主正常结束后，未隔离的对照子进程被终止，独立子进程仍无包／无 Job 并继续更新心跳；随后受控停止测试子进程。还覆盖参数转义、防递归和迁移不覆盖。
- 隔离 STA/WPF 检查增加首次排队异步任务返回原 UI 线程的回归。测试不使用用户实例 IPC、设备或个人配置。

构建后直接运行启动检查 EXE。`dotnet run` 会增加 MSBuild 自己的受限 Job，不把该测试启动器限制当作产品崩溃。没有通过关闭或更新 Codex、系统注销、重新安装包来验证旧包现场；实际部署仍须核对常驻主进程的 `Launch` 与 `DataLocation`。

## 本机部署验证

从最终 `dist` 路径恢复后，主进程实际记录 `PackageFullName=null`、`InJob=false`；文件句柄确认的日志目录为独立的 `%LOCALAPPDATA%\PeripheralBattery`。导入的设置文件与原包缓存文件哈希一致，未启用或修改开机启动。鼠标与 MX 10.0 读取正常，卡片和详情已核对。

用户随后明确要求两个工具均不显示普通任务栏应用图标，因此卡片、详情以及 `--inspect-ui` 均强制 `ShowInTaskbar=false`，按钮或外部系统最小化均收起到托盘。共享 Computer Use 请求发生冲突后停止进一步桌面操作；最终无按钮状态通过隔离 WPF 检查验证，并恢复普通后台启动。未操作或修改 AI 用量工具。
