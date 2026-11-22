using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siming.Core.Configuration;

/// <summary>
/// 一条日志字段提取规则：从条目自己的输出里取一个值。
/// 配置里可以写成字符串简写，等价于只给 regex。
/// </summary>
[JsonConverter(typeof(ExtractRuleConverter))]
public sealed class ExtractRule
{
    public string Regex { get; set; } = "";

    /// <summary>值不进入日志、诊断、控制接口与 MCP，只能被动作使用。</summary>
    public bool Secret { get; set; }

    /// <summary>first（默认，命中一次即冻结）/ latest（每次命中都更新）。</summary>
    public string Mode { get; set; } = ExtractModes.First;

    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsLatest => string.Equals(Mode?.Trim(), ExtractModes.Latest, StringComparison.OrdinalIgnoreCase);

    /// <summary>用于热重载时判断该字段是否需要复位：正则或行为变了就得重来。</summary>
    [JsonIgnore]
    public string Signature => $"{Regex}\u001f{ExtractModes.Normalize(Mode)}\u001f{Secret}";
}

public static class ExtractModes
{
    public const string First = "first";

    public const string Latest = "latest";

    public static bool IsKnown(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or First => true,
        Latest => true,
        _ => false,
    };

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Latest, StringComparison.OrdinalIgnoreCase) ? Latest : First;
}

/// <summary>
/// 允许 <c>"url": "regex"</c> 与 <c>"url": { "regex": ... }</c> 两种写法；
/// 输出时保持简写，除非显式设置了 secret / mode。
/// </summary>
internal sealed class ExtractRuleConverter : JsonConverter<ExtractRule>
{
    public override ExtractRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ExtractRule { Regex = reader.GetString() ?? "" };
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            // 允许整段留空；加载后按「缺少 regex」跳过，不阻断配置。
            return new ExtractRule();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("extract 的字段必须是字符串或对象");
        }

        var rule = new ExtractRule();
        using var document = JsonDocument.ParseValue(ref reader);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "regex":
                    rule.Regex = property.Value.GetString() ?? "";
                    break;
                case "secret":
                    rule.Secret = property.Value.ValueKind == JsonValueKind.True;
                    break;
                case "mode":
                    rule.Mode = property.Value.GetString() ?? ExtractModes.First;
                    break;
                default:
                    (rule.Extra ??= [])[property.Name] = property.Value.Clone();
                    break;
            }
        }

        return rule;
    }

    public override void Write(Utf8JsonWriter writer, ExtractRule value, JsonSerializerOptions options)
    {
        var plain = !value.Secret
            && !value.IsLatest
            && (value.Extra is null || value.Extra.Count == 0);

        if (plain)
        {
            writer.WriteStringValue(value.Regex);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("regex", value.Regex);

        if (value.Secret)
        {
            writer.WriteBoolean("secret", true);
        }

        if (value.IsLatest)
        {
            writer.WriteString("mode", ExtractModes.Latest);
        }

        if (value.Extra is not null)
        {
            foreach (var (key, element) in value.Extra)
            {
                writer.WritePropertyName(key);
                element.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}
