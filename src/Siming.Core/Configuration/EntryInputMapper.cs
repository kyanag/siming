using Siming.Core.Control;

namespace Siming.Core.Configuration;

/// <summary>
/// 把控制接口/MCP 传来的 EntryInput 合并成 EntryConfig。
/// 新增与更新共用；更新时未提供的字段保持原值。
/// </summary>
public static class EntryInputMapper
{
    public static EntryConfig Apply(EntryInput input, EntryConfig? existing)
    {
        var name = (input.Name ?? existing?.Name ?? "").Trim();
        var cwd = (input.Cwd ?? existing?.Cwd ?? "").Trim();
        var command = (input.Command ?? existing?.Command ?? "").Trim();

        if (name.Length == 0)
        {
            throw new InvalidOperationException("名称不能为空");
        }

        if (cwd.Length == 0)
        {
            throw new InvalidOperationException("工作目录不能为空");
        }

        if (command.Length == 0)
        {
            throw new InvalidOperationException("命令不能为空");
        }

        var shell = !string.IsNullOrWhiteSpace(input.Shell)
            ? input.Shell.Trim()
            : existing?.Shell ?? "cmd";
        var encoding = !string.IsNullOrWhiteSpace(input.Encoding)
            ? input.Encoding.Trim()
            : existing?.Encoding ?? "utf-8";

        var entry = new EntryConfig
        {
            Id = existing?.Id,
            Name = name,
            Cwd = cwd,
            Command = command,
            Shell = shell,
            Encoding = encoding,
            Env = input.Env ?? existing?.Env,
            AutoStart = input.AutoStart ?? existing?.AutoStart ?? false,
            Actions = input.Actions ?? existing?.Actions,
            Extra = existing?.Extra,
        };

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = string.IsNullOrWhiteSpace(input.Id)
                ? Guid.NewGuid().ToString("N")
                : input.Id.Trim();
        }

        return entry;
    }

    public static EntryStatus ToStatus(EntryConfig config, string state = "unknown") => new()
    {
        Id = config.Id,
        Name = config.Name,
        Cwd = config.Cwd,
        Command = config.Command,
        Shell = config.Shell,
        Encoding = config.Encoding,
        Env = config.Env,
        AutoStart = config.AutoStart,
        State = state,
    };
}
