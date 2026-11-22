using System.Text;
using Siming.Core.Configuration;

namespace Siming.Core.Processes;

internal static class CommandLineBuilder
{
    public static (string? Application, string CommandLine) Build(EntryConfig entry)
    {
        return entry.ShellKind switch
        {
            ShellKind.Cmd => BuildCmd(entry.Command),
            ShellKind.PowerShell => BuildPowerShell(entry.Command),
            ShellKind.Pwsh => BuildPwsh(entry.Command),
            _ => (null, entry.Command),
        };
    }

    private static (string, string) BuildCmd(string command)
    {
        var comspec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comspec) || !File.Exists(comspec))
        {
            comspec = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        // /s 让 cmd 去掉最外层引号后原样执行，避免命令内的引号被二次解释。
        return (comspec, $"/d /s /c \"{command}\"");
    }

    private static (string, string) BuildPowerShell(string command)
    {
        var path = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(path))
        {
            path = "powershell.exe";
        }

        return (path, $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {Encode(command)}");
    }

    private static (string, string) BuildPwsh(string command)
    {
        var path = FindOnPath("pwsh.exe")
            ?? throw new FileNotFoundException("找不到 pwsh.exe，请确认 PowerShell 7 已安装并在 PATH 中");

        return (path, $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {Encode(command)}");
    }

    private static string Encode(string command) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // PATH 中的非法片段直接跳过。
            }
        }

        return null;
    }
}
