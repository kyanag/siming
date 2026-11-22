namespace Siming.Core.Configuration;

public static class EntryValidator
{
    /// <summary>运行前校验；返回 null 表示通过。</summary>
    public static string? ValidateForRun(EntryConfig entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Cwd))
        {
            return "工作目录为空";
        }

        if (!Directory.Exists(entry.Cwd))
        {
            return $"工作目录不存在：{entry.Cwd}";
        }

        if (string.IsNullOrWhiteSpace(entry.Command))
        {
            return "命令为空";
        }

        if (!ShellKinds.IsKnown(entry.Shell))
        {
            return $"不支持的 shell：{entry.Shell}";
        }

        if (!EncodingNames.IsSupported(entry.Encoding))
        {
            return $"不支持的编码：{entry.Encoding}";
        }

        if (entry.Env is not null)
        {
            foreach (var key in entry.Env.Keys)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Contains('='))
                {
                    return $"非法环境变量名：{key}";
                }
            }
        }

        return null;
    }

    /// <summary>配置整体校验；返回 null 表示通过。</summary>
    public static string? ValidateConfig(AppConfig config)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                return "存在名称为空的条目";
            }

            if (!seenNames.Add(entry.Name.Trim()))
            {
                return $"条目名称重复：{entry.Name}";
            }

            if (!string.IsNullOrWhiteSpace(entry.Id) && !seenIds.Add(entry.Id.Trim()))
            {
                return $"条目 id 重复：{entry.Id}";
            }

            if (!ShellKinds.IsKnown(entry.Shell))
            {
                return $"条目「{entry.Name}」的 shell 非法：{entry.Shell}";
            }

            if (!EncodingNames.IsSupported(entry.Encoding))
            {
                return $"条目「{entry.Name}」的编码非法：{entry.Encoding}";
            }
        }

        return null;
    }
}
