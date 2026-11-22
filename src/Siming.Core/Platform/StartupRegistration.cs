using Microsoft.Win32;

namespace Siming.Core.Platform;

/// <summary>
/// 当前用户的开机启动项（HKCU\...\Run）。这是软件唯一写入注册表的地方。
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Siming";

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" --tray";

    public static bool IsEnabled(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return string.Equals(value, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, string executablePath, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "无法打开开机启动项注册表键";
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
