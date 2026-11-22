# 开发与发布

面向需要构建、修改或发布司命的开发者。普通使用请看 [README](../README.md)。

## 环境

- Windows 10 / 11 x64
- .NET 10 SDK
- 目标框架：`net10.0-windows`（WinForms）

## 构建与运行

```powershell
dotnet build Siming.slnx
dotnet run --project src/Siming.App
```

Debug 可执行文件：

```text
src/Siming.App/bin/Debug/net10.0-windows/Siming.exe
```

静默启动（只进托盘）：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --tray
```

以 stdio MCP 服务运行：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --mcp
```

MCP 工具、客户端配置和控制协议见 [mcp.md](mcp.md)。

## 测试

```powershell
dotnet test tests/Siming.Tests
```

覆盖配置读写与未知字段保留、坏 JSON 保留旧配置、子进程输出捕获、整树终止、重启、启动失败提示、MCP 配置降级、控制接口鉴权等。

## 自检

在无控制台的真实 WinExe 环境里验证「启动 → Ctrl+C → 整树终止」：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --selftest
Get-Content $env:TEMP\siming-selftest.log
```

预期 `result: PASS`，且 `graceful=True`（通常在 100 ms 左右；系统负载高时可能接近 1 s）。

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

默认发布到 `D:\tools\Siming`：

- **默认（框架依赖，也是 `-Thin`）**：单文件约 4 MB，内存低；目标机器需要 .NET 10 Desktop Runtime
- **`-SelfContained`**：自包含单文件约 51 MB，目标机器无需 .NET；压缩单文件的内存占用较高
- **`-Target <目录>`**：指定其他输出目录

```powershell
# 默认：框架依赖
.\scripts\publish.ps1

# 自包含，给没装 .NET 的机器
.\scripts\publish.ps1 -SelfContained

# 指定目录
.\scripts\publish.ps1 -Target D:\tools\SimingTest
```

发布前需要退出正在运行的司命。如果 AI 客户端正通过 `--mcp` 使用司命，也要先重启该客户端，否则 exe 会被占用。发布脚本会保留目标目录已有的 `processes.json` 和 `mcp.token`，并清理旧版遗留的 `Siming.Mcp.exe`。

磁盘和内存的取舍：

| 方式 | 磁盘 | 主程序私有内存 | 每个 MCP 进程 |
|---|---|---|---|
| 框架依赖（默认） | ~4 MB | ~15–20 MB | ~11–12 MB |
| 自包含 + 压缩 | ~51 MB | ~50–70 MB | ~50 MB |

## GitHub Actions

`.github/workflows/release.yml`：

- push 到 `main` / `master`：构建、测试，并更新滚动预发布 `latest`
- push `v*` 标签：构建并创建对应版本的正式 Release
- 手动触发：按当前 ref 执行

每个 Release 包含：

- `Siming-win-x64.zip`：框架依赖版
- `Siming-win-x64-selfcontained.zip`：自包含版
- `SHA256SUMS.txt`：ZIP 校验和

首次使用前，在仓库 `Settings → Actions → General → Workflow permissions` 选择 **Read and write permissions**，否则 `GITHUB_TOKEN` 无法创建 Release 或更新 `latest` 标签。

> `latest` 是滚动标签，会被强制移动到最新提交；稳定版本请使用 `v*` 标签。

## 结构

```text
src/Siming.Core     配置、日志环形缓冲、日志字段提取、CreateProcess / Job Object、状态机、控制协议
src/Siming.App      WinForms：托盘、主窗口、日志窗、编辑/退出对话框
src/Siming.App/Mcp  MCP 服务、控制客户端、配置降级读写
tests/Siming.Tests  xUnit
```

实现要点：

- 每个条目一个独立 Job Object（`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`），停止或崩溃时整树终止
- 停止先投递 Ctrl+C，5 秒超时后 `TerminateJobObject`
- 单实例 Mutex + 唤起事件
- 配置固定在 `AppContext.BaseDirectory\processes.json`，FileSystemWatcher 防抖重载
- 日志字段提取挂在 `LogBuffer.Appended` 上逐行匹配（输入已剥 ANSI、已解码），正则带 100ms 超时；热重载按字段比对正则，只复位改过的那一项
- 高 DPI：`PerMonitorV2`，构造函数末尾启用 `AutoScaleMode.Dpi`；ListView 列宽单独按 DPI 计算
- 应用图标由内嵌的多尺寸 `app.ico` 提供；托盘图标按运行状态动态生成

## 设计文档

PRD 和 UI 设计稿在项目外部维护，不随源码发布。
