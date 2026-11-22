using System.Text.RegularExpressions;
using Siming.Core.Configuration;

namespace Siming.Core.Extraction;

/// <summary>
/// 单条目的日志字段提取：逐行匹配，命中即更新变量表。
/// 由 <c>LogBuffer.Appended</c> 在输出线程上调用，必须便宜、无锁竞争、绝不抛异常。
/// </summary>
public sealed class LogExtractor
{
    /// <summary>单次匹配上限。病态正则宁可放弃该字段，也不能卡住管道读线程。</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>超长行不参与提取（正常日志很少超过这个长度）。</summary>
    public const int MaxLineLength = 8 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, Field> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _active;

    public LogExtractor(IReadOnlyDictionary<string, ExtractRule>? rules)
    {
        Rebuild(rules, preserveValues: false);
    }

    /// <summary>规则非法时的说明；null 表示全部可用。</summary>
    public string? ConfigurationError { get; private set; }

    public bool HasFields
    {
        get
        {
            lock (_gate)
            {
                return _fields.Count > 0;
            }
        }
    }

    /// <summary>条目启动时调用：清空全部值并解除冻结。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _values.Clear();
            foreach (var field in _fields.Values)
            {
                field.Frozen = false;
            }

            UpdateActive();
        }
    }

    /// <summary>热重载：规则未变的字段保留值与冻结状态，规则变了的字段复位。</summary>
    public void Reconfigure(IReadOnlyDictionary<string, ExtractRule>? rules)
    {
        Rebuild(rules, preserveValues: true);
    }

    public void OnLine(string line)
    {
        if (!Volatile.Read(ref _active))
        {
            return;
        }

        if (string.IsNullOrEmpty(line) || line.Length > MaxLineLength)
        {
            return;
        }

        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            var waiting = false;
            foreach (var field in _fields.Values)
            {
                if (field.Pattern is null || field.Frozen)
                {
                    continue;
                }

                if (Match(field, line, out var value))
                {
                    _values[field.Name] = value;
                    if (!field.Latest)
                    {
                        field.Frozen = true;
                    }
                }

                if (!field.Frozen)
                {
                    waiting = true;
                }
            }

            Volatile.Write(ref _active, waiting);
        }
    }

    public bool TryGetValue(string name, out string value)
    {
        lock (_gate)
        {
            return _values.TryGetValue(name, out value!);
        }
    }

    public Dictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            var ok = ch is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_' or '-';

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private void Rebuild(IReadOnlyDictionary<string, ExtractRule>? rules, bool preserveValues)
    {
        var errors = new List<string>();

        lock (_gate)
        {
            var next = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase);

            if (rules is not null)
            {
                foreach (var (name, rule) in rules)
                {
                    if (!IsValidName(name))
                    {
                        errors.Add($"字段名「{name}」只能用字母、数字、下划线和短横线");
                        continue;
                    }

                    var signature = rule.Signature;
                    if (preserveValues
                        && _fields.TryGetValue(name, out var existing)
                        && existing.Pattern is not null
                        && existing.Signature == signature)
                    {
                        next[name] = existing;
                        continue;
                    }

                    _values.Remove(name);
                    var field = Create(name, rule, errors);
                    if (field is not null)
                    {
                        next[name] = field;
                    }
                }
            }

            foreach (var name in _fields.Keys)
            {
                if (!next.ContainsKey(name))
                {
                    _values.Remove(name);
                }
            }

            _fields.Clear();
            foreach (var (name, field) in next)
            {
                _fields[name] = field;
            }

            ConfigurationError = errors.Count == 0 ? null : string.Join("；", errors);
            UpdateActive();
        }
    }

    private static Field? Create(string name, ExtractRule rule, List<string> errors)
    {
        var field = new Field
        {
            Name = name,
            Latest = rule.IsLatest,
            Signature = rule.Signature,
        };

        if (string.IsNullOrWhiteSpace(rule.Regex))
        {
            errors.Add($"字段「{name}」缺少 regex");
            return null;
        }

        try
        {
            field.Pattern = new Regex(rule.Regex, RegexOptions.CultureInvariant, MatchTimeout);
            return field;
        }
        catch (ArgumentException ex)
        {
            errors.Add($"字段「{name}」的正则非法：{ex.Message}");
            return null;
        }
    }

    private static bool Match(Field field, string line, out string value)
    {
        value = "";

        try
        {
            var match = field.Pattern!.Match(line);
            if (!match.Success)
            {
                return false;
            }

            var captured = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            if (string.IsNullOrWhiteSpace(captured))
            {
                return false;
            }

            value = captured;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // 冻结该字段：否则每一行都要付出一次超时代价，等于把输出线程掐死。
            field.Frozen = true;
            return false;
        }
    }

    private void UpdateActive()
    {
        var waiting = false;
        foreach (var field in _fields.Values)
        {
            if (field.Pattern is not null && !field.Frozen)
            {
                waiting = true;
                break;
            }
        }

        Volatile.Write(ref _active, waiting);
    }

    private sealed class Field
    {
        public string Name = "";

        public Regex? Pattern;

        public bool Latest;

        public string Signature = "";

        public bool Frozen;
    }
}
