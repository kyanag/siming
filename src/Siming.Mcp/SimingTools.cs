using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siming.Core.Control;

namespace Siming.Mcp;

[McpServerToolType]
public static class SimingTools
{
    [McpServerTool(Name = "siming_list_entries", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("列出司命中的所有进程条目及其运行状态。appRunning 表示主程序进程是否存在，controlConnected 表示控制接口是否可用；未连接时 state 为 unknown。")]
    public static async Task<string> ListEntries(
        ControlClient client,
        McpConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await client.SendAsync(
                new ControlRequest { Cmd = "list" },
                autoLaunch: false,
                cancellationToken).ConfigureAwait(false);
            return Pretty(data);
        }
        catch (ControlUnavailableException)
        {
            var appRunning = Process.GetProcessesByName("Siming").Length > 0;
            return Pretty(new
            {
                appRunning,
                controlConnected = false,
                entries = config.ListStatuses(),
                note = "控制接口不可用，只能读取配置；start/stop 等控制操作会尝试拉起司命。",
            });
        }
    }

    [McpServerTool(Name = "siming_add_entry", Destructive = false, OpenWorld = false)]
    [Description("向司命添加一个进程条目。cwd 必须是已存在的目录；command 会交给 shell 执行。司命主程序未运行时会直接写入 processes.json。")]
    public static async Task<string> AddEntry(
        ControlClient client,
        McpConfig config,
        [Description("条目名称，必须唯一")] string name,
        [Description("工作目录，必须是已存在的绝对路径")] string cwd,
        [Description("要执行的命令，例如 npm run dev 或 python app.py")] string command,
        [Description("shell：cmd / powershell / pwsh / none，默认 cmd")] string? shell = null,
        [Description("stdout/stderr 编码：utf-8 / gbk，默认 utf-8")] string? encoding = null,
        [Description("环境变量覆盖；值为 null 表示删除该变量")] Dictionary<string, string?>? env = null,
        [Description("是否随司命启动，默认 false")] bool? autoStart = null,
        CancellationToken cancellationToken = default)
    {
        var input = new EntryInput
        {
            Name = name,
            Cwd = cwd,
            Command = command,
            Shell = shell,
            Encoding = encoding,
            Env = env,
            AutoStart = autoStart,
        };

        try
        {
            var data = await client.SendAsync(
                new ControlRequest { Cmd = "add", Entry = input },
                autoLaunch: false,
                cancellationToken).ConfigureAwait(false);
            return Pretty(data);
        }
        catch (ControlUnavailableException)
        {
            return Pretty(new
            {
                entry = config.Add(input),
                note = "司命控制接口不可用（主程序未运行或接口未就绪），已直接写入配置；下次启动司命后生效。",
            });
        }
    }

    [McpServerTool(Name = "siming_update_entry", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("修改司命中的一个条目，只更新传入的字段。正在运行的进程不会自动重启，需要再调用 siming_restart_entry。")]
    public static async Task<string> UpdateEntry(
        ControlClient client,
        McpConfig config,
        [Description("条目 id 或名称")] string key,
        [Description("新名称")] string? name = null,
        [Description("新的工作目录")] string? cwd = null,
        [Description("新的命令")] string? command = null,
        [Description("shell：cmd / powershell / pwsh / none")] string? shell = null,
        [Description("输出编码：utf-8 / gbk")] string? encoding = null,
        [Description("环境变量覆盖；传空对象表示清空，值为 null 表示删除变量")] Dictionary<string, string?>? env = null,
        [Description("是否随司命启动")] bool? autoStart = null,
        CancellationToken cancellationToken = default)
    {
        var input = new EntryInput
        {
            Name = name,
            Cwd = cwd,
            Command = command,
            Shell = shell,
            Encoding = encoding,
            Env = env,
            AutoStart = autoStart,
        };

        try
        {
            var data = await client.SendAsync(
                new ControlRequest { Cmd = "update", Key = key, Entry = input },
                autoLaunch: false,
                cancellationToken).ConfigureAwait(false);
            return Pretty(data);
        }
        catch (ControlUnavailableException)
        {
            return Pretty(new
            {
                entry = config.Update(key, input),
                note = "司命控制接口不可用（主程序未运行或接口未就绪），已直接写入配置；下次启动司命后生效。",
            });
        }
    }

    [McpServerTool(Name = "siming_remove_entry", Destructive = true, OpenWorld = false)]
    [Description("从司命删除一个条目。默认会先停止正在运行的进程。司命主程序未运行时只删除配置。")]
    public static async Task<string> RemoveEntry(
        ControlClient client,
        McpConfig config,
        [Description("条目 id 或名称")] string key,
        [Description("如果进程正在运行，是否先停止，默认 true")] bool stop = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await client.SendAsync(
                new ControlRequest { Cmd = "remove", Key = key, Stop = stop },
                autoLaunch: false,
                cancellationToken).ConfigureAwait(false);
            return Pretty(data);
        }
        catch (ControlUnavailableException)
        {
            return Pretty(config.Remove(key));
        }
    }

    [McpServerTool(Name = "siming_start_entry", Destructive = false, OpenWorld = false)]
    [Description("启动一个条目。司命主程序未运行时会自动拉起（只进托盘）。")]
    public static async Task<string> StartEntry(
        ControlClient client,
        [Description("条目 id 或名称")] string key,
        CancellationToken cancellationToken)
    {
        var data = await client.SendAsync(
            new ControlRequest { Cmd = "start", Key = key },
            autoLaunch: true,
            cancellationToken).ConfigureAwait(false);
        return Pretty(data);
    }

    [McpServerTool(Name = "siming_stop_entry", Destructive = true, OpenWorld = false)]
    [Description("停止一个条目的进程；先尝试 Ctrl+C，超时后整树强杀。")]
    public static async Task<string> StopEntry(
        ControlClient client,
        [Description("条目 id 或名称")] string key,
        CancellationToken cancellationToken)
    {
        var data = await client.SendAsync(
            new ControlRequest { Cmd = "stop", Key = key },
            autoLaunch: true,
            cancellationToken).ConfigureAwait(false);
        return Pretty(data);
    }

    [McpServerTool(Name = "siming_restart_entry", Destructive = true, OpenWorld = false)]
    [Description("重启一个条目：停止并等待退出，然后重新启动。")]
    public static async Task<string> RestartEntry(
        ControlClient client,
        [Description("条目 id 或名称")] string key,
        CancellationToken cancellationToken)
    {
        var data = await client.SendAsync(
            new ControlRequest { Cmd = "restart", Key = key },
            autoLaunch: true,
            cancellationToken).ConfigureAwait(false);
        return Pretty(data);
    }

    [McpServerTool(Name = "siming_get_logs", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("读取条目最近的日志。日志只存在内存中，司命未运行时无法读取。")]
    public static async Task<string> GetLogs(
        ControlClient client,
        [Description("条目 id 或名称")] string key,
        [Description("返回最近多少行，1-5000，默认 200")] int lines = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await client.SendAsync(
                new ControlRequest
                {
                    Cmd = "logs",
                    Key = key,
                    Lines = Math.Clamp(lines, 1, 5000),
                },
                autoLaunch: false,
                cancellationToken).ConfigureAwait(false);
            return Pretty(data);
        }
        catch (ControlUnavailableException)
        {
            throw new McpException("司命主程序未运行，日志只在内存中，无法读取。");
        }
    }

    private static string Pretty(object value) =>
        JsonSerializer.Serialize(value, ControlProtocol.PrettyJsonOptions);
}
