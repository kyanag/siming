using Microsoft.Win32;

namespace Siming.Core.Platform;

/// <summary>开机启动项的实际状态。注意它和配置里的 <c>startWithWindows</c> 是两回事。</summary>
public enum StartupState
{
    /// <summary>注册表里没有这个启动项。</summary>
    Disabled,

    /// <summary>启动项存在、指向当前程序，并且没有被系统禁用。</summary>
    Enabled,

    /// <summary>启动项存在，但被系统或任务管理器标记为禁用。</summary>
    DisabledBySystem,

    /// <summary>启动项存在，但指向别的 exe。</summary>
    OtherPath,

    /// <summary>读取注册表失败。</summary>
    Error,
}

public sealed record StartupStatus(StartupState State, string? Command, string? Error)
{
    /// <summary>登录时真的会拉起司命。</summary>
    public bool IsEffective => State == StartupState.Enabled;
}

/// <summary>启动项需要做的动作。</summary>
public enum StartupAction
{
    None,
    Register,
    Unregister,
}

/// <summary>
/// 当前用户的开机启动项（HKCU\...\Run）。这是软件唯一写入注册表的地方。
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Siming";

    /// <summary>注册表的完整路径，用于界面展示。</summary>
    public static string RegistryPath => @"HKEY_CURRENT_USER\" + RunKeyPath;

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" --tray";

    /// <summary>启动项里的命令是否就是我们期望的那条。</summary>
    public static bool IsSameCommand(string? registryValue, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(registryValue))
        {
            return false;
        }

        // 手改过注册表的人可能多写了空格，比较前先去掉首尾空白。
        return string.Equals(registryValue.Trim(), BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Windows 用 StartupApproved 的首字节记录启用状态：偶数为启用，奇数为禁用（常见 0x02 / 0x03）。
    /// 没有这个值时视为启用——这正是系统默认。
    /// </summary>
    public static bool IsApprovedDisabled(byte[]? approvedValue) =>
        approvedValue is { Length: > 0 } && (approvedValue[0] & 1) == 1;

    /// <summary>根据配置和实际状态决定要不要动注册表。</summary>
    public static StartupAction Decide(bool configLoaded, bool enabled, StartupStatus status)
    {
        // 配置没读出来时不要动注册表：否则会把好端端的启动项当成“用户关掉了”。
        if (!configLoaded)
        {
            return StartupAction.None;
        }

        if (enabled)
        {
            return status.State switch
            {
                StartupState.Disabled or StartupState.OtherPath => StartupAction.Register,
                // 被系统禁用通常是用户在任务管理器里手动关掉的，属于人的决定：
                // 只提示，不擅自覆盖，等用户自己点一下再启用。
                _ => StartupAction.None,
            };
        }

        return status.State is StartupState.Enabled or StartupState.DisabledBySystem or StartupState.OtherPath
            ? StartupAction.Unregister
            : StartupAction.None;
    }

    public static StartupStatus Inspect(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return new StartupStatus(StartupState.Disabled, null, null);
            }

            if (!IsSameCommand(value, executablePath))
            {
                return new StartupStatus(StartupState.OtherPath, value, null);
            }

            return IsApprovedDisabled(ReadApprovedValue())
                ? new StartupStatus(StartupState.DisabledBySystem, value, null)
                : new StartupStatus(StartupState.Enabled, value, null);
        }
        catch (Exception ex)
        {
            return new StartupStatus(StartupState.Error, null, ex.Message);
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

            WriteApproved(enabled);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static byte[]? ReadApprovedValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
            return key?.GetValue(ValueName) as byte[];
        }
        catch (Exception)
        {
            // 读不到就按“没被禁用”处理，不因为辅助键读失败把状态判成异常。
            return null;
        }
    }

    private static void WriteApproved(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath, writable: true);
                // 0x02 = 已启用：清掉任务管理器里留下的禁用标记，否则 Run 项在也不会执行。
                key?.SetValue(
                    ValueName,
                    new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                    RegistryValueKind.Binary);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // StartupApproved 只是系统的附加标记，写不进去不影响 Run 项本身。
        }
    }
}
