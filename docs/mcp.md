# 司命 MCP 接入

司命的 `Siming.exe` 同时是 stdio MCP 服务：加 `--mcp` 参数启动即可。把它配置到 AI 客户端后，可以直接让 AI 列出、添加、修改、删除、启动、停止、重启进程条目，并读取日志。

## 工作方式

```
AI 客户端 ──stdio──> Siming.exe --mcp ──TCP 127.0.0.1:17321 + token──> Siming.exe（托盘主程序）
                                    │
                                    └─ 主程序未运行：直接读写 processes.json
```

- **主程序运行中**：MCP 模式走本机控制接口，UI 会实时刷新。
- **主程序未运行**：
  - `list` / `add` / `update` / `remove` 直接读写 `processes.json`，下次启动生效。
  - `start` / `stop` / `restart` 会自动拉起 `Siming.exe --tray`，等控制接口就绪后再执行。
- MCP 模式不占单实例锁，可以和托盘主程序同时运行。
- `Siming.exe`、`processes.json`、`mcp.token` 必须在**同一目录**。发布脚本默认都放到 `D:\tools\Siming`。

## 工具

| 工具 | 作用 |
|---|---|
| `siming_list_entries` | 列出条目与状态；同时返回 `appRunning` / `controlConnected` |
| `siming_add_entry` | 新增条目：name / cwd / command / shell / encoding / env / autoStart |
| `siming_update_entry` | 修改条目，只更新传入字段 |
| `siming_remove_entry` | 删除条目；默认先停止运行中的进程 |
| `siming_start_entry` | 启动条目；主程序未运行会自动拉起 |
| `siming_stop_entry` | 停止条目：Ctrl+C → 5s 超时整树强杀 |
| `siming_restart_entry` | 停止并等待退出后重新启动 |
| `siming_get_logs` | 读取最近 N 行日志（仅主程序运行期间可用） |

> `siming_update_entry` 不会自动重启正在运行的进程。改完命令后需要再调用 `siming_restart_entry`。

## 客户端配置

所有客户端都是启动同一个 exe，只是参数不同。旧版独立服务 `Siming.Mcp.exe` 已移除。

### Claude Desktop

编辑 `%APPDATA%\Claude\claude_desktop_config.json`：

```json
{
  "mcpServers": {
    "siming": {
      "command": "D:\\tools\\Siming\\Siming.exe",
      "args": ["--mcp"]
    }
  }
}
```

### Cursor

编辑 `%USERPROFILE%\.cursor\mcp.json`（或项目内 `.cursor/mcp.json`）：

```json
{
  "mcpServers": {
    "siming": {
      "command": "D:\\tools\\Siming\\Siming.exe",
      "args": ["--mcp"]
    }
  }
}
```

### VS Code

编辑 `.vscode/mcp.json`：

```json
{
  "servers": {
    "siming": {
      "type": "stdio",
      "command": "D:\\tools\\Siming\\Siming.exe",
      "args": ["--mcp"]
    }
  }
}
```

### OpenCode

OpenCode 的 `mcp add` 会把 `--mcp` 误当成它自己的参数，直接编辑全局配置 `~/.config/opencode/opencode.json`（或项目配置）：

```json
{
  "mcp": {
    "servers": {
      "siming": {
        "type": "local",
        "command": ["D:\\tools\\Siming\\Siming.exe", "--mcp"]
      }
    }
  }
}
```

保存后重启 OpenCode。`opencode mcp list` 应显示 `✓ siming connected`。

配置后重启客户端，工具列表里应出现 `siming_*`。

## 使用示例

- 「把 `D:\models\run_qwen_4b_server.ps1` 加到司命，cwd 用 `D:\models`」
  → `siming_add_entry(name="Qwen 4B Server", cwd="D:\\models", command="& 'D:\\models\\run_qwen_4b_server.ps1'", shell="powershell")`
- 「启动 Qwen 4B Server」
  → `siming_start_entry(key="Qwen 4B Server")`
- 「它起来了吗？把最近 50 行日志给我」
  → `siming_get_logs(key="Qwen 4B Server", lines=50)`
- 「改一下端口到 8081，然后重启」
  → `siming_update_entry(...)` + `siming_restart_entry(...)`

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

默认输出自包含的 `Siming.exe`（约 51 MB），目标机器不需要安装 .NET。加 `-Thin` 输出框架依赖版本（约 4 MB），目标机器需要 .NET 10 Desktop Runtime。

## 安全

- 控制接口只绑定 `127.0.0.1`，不会暴露到局域网。
- 每次主程序启动会确保存在 `mcp.token`（32 字节随机数），MCP 模式用它鉴权。
- 令牌文件对同用户进程可读，因此只适合本机个人使用；不要把端口或令牌转发给其他机器。

## 排错

| 现象 | 检查 |
|---|---|
| AI 看不到 `siming_*` 工具 | 客户端配置路径是否正确、是否带了 `args: ["--mcp"]`；重启客户端；先手动运行 `Siming.exe --mcp` 看 stderr |
| `list` 返回 `controlConnected: false` | 主程序没运行，或控制接口未就绪；`start` 会自动拉起 |
| 控制操作报“鉴权失败” | `mcp.token` 与主程序不一致；确认 exe 和配置在同一目录，或重启主程序 |
| 控制操作报端口占用 | 17321 被其他程序占用；关闭冲突程序后重启司命 |
| 日志为空 | 日志只在主程序内存中；主程序未运行或条目尚未输出 |
| 发布时提示 Siming.exe 正在运行 | 退出托盘主程序，并重启正在使用 `--mcp` 的 AI 客户端 |
