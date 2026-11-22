using System.Text.RegularExpressions;

namespace Siming.Core.Logging;

public static partial class Ansi
{
    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex Csi();

    [GeneratedRegex(@"\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)", RegexOptions.Compiled)]
    private static partial Regex Osc();

    public static string Strip(string value)
    {
        if (value.IndexOf('\x1B') < 0 && value.IndexOf('\r') < 0)
        {
            return value;
        }

        var result = Csi().Replace(value, "");
        result = Osc().Replace(result, "");
        return result.Replace("\r", "");
    }
}
