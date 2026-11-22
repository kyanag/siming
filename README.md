# 司命（Siming）

Windows 托盘进程开关面板。给一条 `cwd + 命令`，在托盘里启动、停止、重启，并把整棵进程树停干净。

> 当前状态：M2 完成（核心进程管理 + WinForms 壳 + 完整日常启停）。

## 已实现

- `processes.json` 读写；文件不存在时自动创建；外部修改后自动重载
- 托盘图标：「司」字，空闲单色、有进程运行变绿；动态右键菜单
- 主窗口：条目列表（状态 / 运行时长 / 命令 / 工作目录）
- 启动、停止、重启；停止先发 Ctrl+C，5 秒未退则 Job Object 整树强杀
- 每条目日志窗口：最近 200 行，环形缓冲；向上翻自动暂停，可回到底部
- 退出三选一：结束所有 / 保留进程 / 取消；异常退出由 kill-on-close 兜底
- 单实例；重复启动会唤起已有窗口
- 开机自启开关（托盘菜单与主窗口工具条），写入 `HKCU\...\Run`，登录后以 `--tray` 静默启动

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

M1 还没有条目编辑界面，直接编辑 JSON 即可，保存后会自动重载。

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

- 无新增/编辑/删除条目的界面（M3）
- 托盘图标只在启动时读取任务栏明暗主题
- 运行中修改条目的命令，需重启条目后生效
- 选择「保留进程」后，子进程的 stdout/stderr 管道会随软件退出而关闭；个别程序写入失败可能自行退出
- 快捷键只有主窗口的 F5 / Ctrl+R / Ctrl+L / Esc
