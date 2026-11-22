namespace Siming.Core.Platform;

/// <summary>
/// 主程序单实例相关的命名对象。MCP 模式也用它判断托盘主程序是否在运行。
/// </summary>
public static class SingleInstanceNames
{
    public const string MutexName = @"Local\Siming.SingleInstance";

    public const string ShowEventName = @"Local\Siming.ShowWindow";
}
