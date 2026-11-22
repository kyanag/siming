using System.Runtime.InteropServices;
using System.Text;

namespace Siming.Core.Processes;

internal static class EnvironmentBlock
{
    /// <summary>
    /// 生成 CreateProcess 需要的环境块。无覆盖项时返回 IntPtr.Zero（继承父进程）。
    /// </summary>
    public static IntPtr Build(IReadOnlyDictionary<string, string?>? overrides, out bool ownsHandle)
    {
        ownsHandle = false;
        if (overrides is null || overrides.Count == 0)
        {
            return IntPtr.Zero;
        }

        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            if (item.Key is not string key || key.Length == 0 || key[0] == '=')
            {
                continue;
            }

            values[key] = item.Value as string ?? "";
        }

        foreach (var pair in overrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (pair.Value is null)
            {
                values.Remove(pair.Key);
            }
            else
            {
                values[pair.Key] = pair.Value;
            }
        }

        var builder = new StringBuilder();
        foreach (var pair in values)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }

        builder.Append('\0');
        var bytes = Encoding.Unicode.GetBytes(builder.ToString());
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        ownsHandle = true;
        return pointer;
    }
}
