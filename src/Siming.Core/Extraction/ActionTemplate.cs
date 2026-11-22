using System.Text;

namespace Siming.Core.Extraction;

/// <summary>
/// 动作模板：把 <c>${name}</c> 替换成内置占位符或提取值。
/// 任一占位符取不到值即视为不可用（菜单里置灰），不做部分替换。
/// </summary>
public static class ActionTemplate
{
    public static bool TryResolve(
        string? template,
        IReadOnlyDictionary<string, string?> builtins,
        Func<string, string?>? lookup,
        out string result)
    {
        result = "";
        if (string.IsNullOrEmpty(template))
        {
            return false;
        }

        var builder = new StringBuilder(template.Length + 16);
        var index = 0;

        while (index < template.Length)
        {
            var start = template.IndexOf("${", index, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            builder.Append(template, index, start - index);

            var end = template.IndexOf('}', start + 2);
            if (end < 0)
            {
                return false;
            }

            var name = template[(start + 2)..end].Trim();
            if (name.Length == 0)
            {
                return false;
            }

            string? value = null;
            if (!builtins.TryGetValue(name, out value) || value is null)
            {
                value = lookup?.Invoke(name);
            }

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            builder.Append(value);
            index = end + 1;
        }

        if (builder.Length == 0)
        {
            return false;
        }

        result = builder.ToString();
        return true;
    }
}
