using ModelContextProtocol;
using Siming.Core.Configuration;
using Siming.Core.Control;

namespace Siming.Mcp;

/// <summary>
/// 主程序没运行时的降级路径：直接读写 processes.json。
/// </summary>
public sealed class McpConfig(McpPaths paths)
{
    public string ConfigPath => paths.ConfigPath;

    public EntryStatus[] ListStatuses()
    {
        var config = Load();
        return config.Entries
            .Select(entry => EntryInputMapper.ToStatus(entry))
            .ToArray();
    }

    public EntryStatus Add(EntryInput input)
    {
        var config = Load();
        var entry = EntryInputMapper.Apply(input, existing: null);
        EnsureRunnable(entry);
        config.Entries.Add(entry);
        Save(config);
        return EntryInputMapper.ToStatus(entry);
    }

    public EntryStatus Update(string key, EntryInput input)
    {
        var config = Load();
        var index = FindIndex(config, key);
        if (index < 0)
        {
            throw new McpException($"条目不存在：{key}");
        }

        var updated = EntryInputMapper.Apply(input, config.Entries[index]);
        EnsureRunnable(updated);
        config.Entries[index] = updated;
        Save(config);
        return EntryInputMapper.ToStatus(updated);
    }

    public object Remove(string key)
    {
        var config = Load();
        var index = FindIndex(config, key);
        if (index < 0)
        {
            throw new McpException($"条目不存在：{key}");
        }

        var removed = config.Entries[index];
        config.Entries.RemoveAt(index);
        Save(config);

        return new
        {
            removed = true,
            id = removed.Id,
            name = removed.Name,
            note = "司命控制接口不可用，只修改了配置。",
        };
    }

    private AppConfig Load()
    {
        using var store = new ConfigStore(paths.ConfigPath);
        var result = store.Load();
        if (!result.Success || result.Config is null)
        {
            throw new McpException(result.Error ?? "读取配置失败");
        }

        return result.Config;
    }

    private void Save(AppConfig config)
    {
        using var store = new ConfigStore(paths.ConfigPath);
        if (!store.TrySave(config, out var error))
        {
            throw new McpException(error ?? "保存配置失败");
        }
    }

    private static void EnsureRunnable(EntryConfig entry)
    {
        var error = EntryValidator.ValidateForRun(entry);
        if (error is not null)
        {
            throw new McpException(error);
        }
    }

    private static int FindIndex(AppConfig config, string key)
    {
        for (var index = 0; index < config.Entries.Count; index++)
        {
            var entry = config.Entries[index];
            if (string.Equals(entry.Id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Name, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
