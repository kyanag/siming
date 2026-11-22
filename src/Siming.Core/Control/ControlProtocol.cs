using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Siming.Core.Configuration;

namespace Siming.Core.Control;

/// <summary>
/// 司命主程序与 MCP 服务之间的本机控制协议：TCP + 按行 JSON。
/// </summary>
public sealed class ControlHello
{
    public string Token { get; set; } = "";
}

public sealed class ControlHelloResponse
{
    public bool Ok { get; set; }

    public string? Error { get; set; }
}

public sealed class ControlRequest
{
    public int Id { get; set; }

    /// <summary>ping / list / logs / add / update / remove / start / stop / restart</summary>
    public string Cmd { get; set; } = "";

    public string? Key { get; set; }

    public int? Lines { get; set; }

    public bool? Stop { get; set; }

    public EntryInput? Entry { get; set; }
}

public sealed class ControlResponse
{
    public int Id { get; set; }

    public bool Ok { get; set; }

    public object? Data { get; set; }

    public string? Error { get; set; }
}

public sealed class EntryInput
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Cwd { get; set; }

    public string? Command { get; set; }

    public string? Shell { get; set; }

    public string? Encoding { get; set; }

    public Dictionary<string, string?>? Env { get; set; }

    public bool? AutoStart { get; set; }

    public List<ActionConfig>? Actions { get; set; }
}

public sealed class EntryStatus
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Cwd { get; set; }

    public string? Command { get; set; }

    public string? Shell { get; set; }

    public string? Encoding { get; set; }

    public Dictionary<string, string?>? Env { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>stopped / starting / running / stopping / unknown（主程序未运行）</summary>
    public string State { get; set; } = "unknown";

    public int? Pid { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public long? UptimeSeconds { get; set; }

    public int? LastExitCode { get; set; }

    public DateTimeOffset? LastExitedAt { get; set; }

    public string? LastError { get; set; }

    public bool IsOrphan { get; set; }

    public long LogTotal { get; set; }

    public long LogDropped { get; set; }

    [JsonIgnore]
    public string Key => string.IsNullOrWhiteSpace(Id) ? Name ?? "" : Id;
}

public static class ControlProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonOptions)
    {
        WriteIndented = true,
    };
}
