# 司命（Siming）

Windows 托盘进程开关面板。给一条 `cwd + 命令`，在托盘里启动、停止、重启，并把整棵进程树停干净。

> 当前状态：v1.1 完成（M1–M4）。

## 已实现

- `processes.json` 读写；文件不存在时自动创建；外部修改后自动重载
- 托盘图标：「司」字，空闲单色、有进程运行变绿；动态右键菜单
- 主窗口：条目列表（状态 / 运行时长 / 命令 / 工作目录）
- 条目增删改：名称、工作目录、命令、shell、编码、环境变量、自启动；上移/下移
- 启动、停止、重启；停止先发 Ctrl+C，5 秒未退则 Job Object 整树强杀
- 每条目日志窗口：最近 200 行，环形缓冲；向上翻自动暂停，可回到底部
- 退出三选一：结束所有 / 保留进程 / 取消；异常退出由 kill-on-close 兜底
- 单实例；重复启动会唤起已有窗口
- 开机自启开关（托盘菜单与主窗口工具条），写入 `HKCU\...\Run`，登录后以 `--tray` 静默启动
- **MCP 接入**：同一份 `Siming.exe` 带 `--mcp` 模式，可作为 stdio MCP 服务；AI 可直接列出 / 新增 / 修改 / 删除 / 启停条目并读日志，详见 [docs/mcp.md](docs/mcp.md)

## 构建与运行

```powershell
dotnet build Siming.slnx
dotnet run --project src/Siming.App
```

可执行文件：`src/Siming.App/bin/Debug/net10.0-windows/Siming.exe`

静默启动（只进托盘）：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --tray
```

以 stdio MCP 服务运行（供 AI 客户端调用）：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --mcp
```

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

默认发布到 `D:\tools\Siming`，产出：

- `Siming.exe`：托盘主程序 + stdio MCP 模式（`Siming.exe --mcp`），单文件自包含，约 51 MB
- `processes.json`：目标目录不存在时从开发配置或示例播种

目标目录已有配置时不会覆盖；发布前需要先退出正在运行的司命。如果 AI 客户端正通过 `--mcp` 使用司命，也要先重启该客户端，否则 exe 被占用。可以用 `-Target` 指定其他目录。

加 `-Thin` 可发布框架依赖版本（约 4 MB），目标机器需要安装 .NET 10 Desktop Runtime：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Thin
```

MCP 客户端配置见 [docs/mcp.md](docs/mcp.md)。

要求 .NET 10 SDK（项目目标 `net10.0-windows`）。

## 配置

配置文件固定为 **exe 同目录** 的 `processes.json`，不写 `%APPDATA%`。参考 [processes.sample.json](processes.sample.json)：

```json
{
  "version": 1,
  "settings": {
    "logLines": 200,
    "startWithWindows": false
  },
  "entries": [
    {
      "id": "blog-dev",
      "name": "Blog Dev",
      "cwd": "D:\\projects\\blog",
      "command": "npm run dev",
      "shell": "cmd",
      "encoding": "utf-8",
      "env": { "PORT": "3000" },
      "autoStart": false
    }
  ]
}
```

| 字段 | 说明 |
|---|---|
| `shell` | `cmd`（默认）/ `powershell` / `pwsh` / `none`（直接执行 exe） |
| `encoding` | `utf-8`（默认）/ `gbk` |
| `env` | 合并进系统环境，值为 `null` 表示删除该变量 |
| `autoStart` | 软件启动后自动拉起 |
| `actions` | v2 快捷操作，v1 不展示但保存时原样保留 |

M1–M3 已包含条目编辑界面；也可以直接编辑 JSON，保存后自动重载。界面首次保存没有 `id` 的条目时会自动生成并回写，确保改名不会改变运行时身份。

## 自检

在无控制台的真实 WinExe 环境里验证「启动 → Ctrl+C → 整树终止」：

```powershell
& .\src\Siming.App\bin\Debug\net10.0-windows\Siming.exe --selftest
Get-Content $env:TEMP\siming-selftest.log
```

预期 `result: PASS`，且 `graceful=True`（通常在 100ms 内）。

## 测试

```powershell
dotnet test tests/Siming.Tests
```

覆盖：配置读写与未知字段保留、坏 JSON 保留旧配置、子进程输出捕获、整树终止、重启、启动失败提示。

## 结构

```
src/Siming.Core     配置、日志环形缓冲、CreateProcess/Job Object、状态机
src/Siming.App      WinForms：托盘、主窗口、日志窗、编辑/退出对话框
tests/Siming.Tests  xUnit
```

设计文档在 `D:\projects\ideas\idea3\docs`（PRD、UI 稿）。

## 已知限制

- 托盘图标只在启动时读取任务栏明暗主题
- 运行中修改条目的命令，会提示是否立即重启
- 选择「保留进程」后，子进程的 stdout/stderr 管道会随软件退出而关闭；个别程序写入失败可能自行退出
- 快捷键只在主窗口生效：`F5` 启停、`Ctrl+R` 重启、`Ctrl+L` 日志、`Ctrl+N` 新增、`Enter`/`F2` 编辑、`Delete` 删除、`Ctrl+↑`/`Ctrl+↓` 排序、`Esc` 隐藏
