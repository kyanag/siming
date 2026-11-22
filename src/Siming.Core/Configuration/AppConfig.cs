using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siming.Core.Configuration;

/// <summary>
/// processes.json 的根对象。
/// </summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public AppSettings Settings { get; set; } = new();

    public List<EntryConfig> Entries { get; set; } = [];

    /// <summary>保留未知字段，避免界面保存时丢配置。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppSettings
{
    public int LogLines { get; set; } = 200;

    public bool StartWithWindows { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public int EffectiveLogLines => Math.Clamp(LogLines, 100, 5000);
}

public sealed class EntryConfig
{
    public string? Id { get; set; }

    public string Name { get; set; } = "";

    public string Cwd { get; set; } = "";

    public string Command { get; set; } = "";

    public string Shell { get; set; } = "cmd";

    public string Encoding { get; set; } = "utf-8";

    public Dictionary<string, string?>? Env { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>v2 快捷操作；v1 不展示，但必须原样保留。</summary>
    public List<ActionConfig>? Actions { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string Key => string.IsNullOrWhiteSpace(Id) ? Name : Id!;

    [JsonIgnore]
    public ShellKind ShellKind => ShellKinds.Parse(Shell);
}

public sealed class ActionConfig
{
    public string Label { get; set; } = "";

    /// <summary>open / copy / command</summary>
    public string Type { get; set; } = "open";

    public string? Target { get; set; }

    public string? Command { get; set; }

    public string? Text { get; set; }

    public bool Confirm { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public enum ShellKind
{
    Cmd,
    PowerShell,
    Pwsh,
    None,
}

public static class ShellKinds
{
    public static ShellKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cmd" or "" or null => ShellKind.Cmd,
        "powershell" or "ps" => ShellKind.PowerShell,
        "pwsh" => ShellKind.Pwsh,
        "none" or "direct" => ShellKind.None,
        _ => ShellKind.Cmd,
    };

    public static bool IsKnown(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cmd" or "" or null => true,
        "powershell" or "ps" => true,
        "pwsh" => true,
        "none" or "direct" => true,
        _ => false,
    };
}
