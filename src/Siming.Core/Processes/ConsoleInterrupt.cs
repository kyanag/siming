using System.Runtime.InteropServices;

namespace Siming.Core.Processes;

public readonly record struct InterruptAttempt(bool Attached, bool Sent, int AttachError, int SendError);

/// <summary>
/// 向子进程的隐藏控制台投递 Ctrl+C，作为优雅停止的第一步。
/// 失败不是错误：调用方会在超时后整树强杀。
/// </summary>
public static class ConsoleInterrupt
{
    public static InterruptAttempt LastAttempt { get; private set; }

    public static bool TrySendCtrlBreak(int processId)
    {
        if (processId <= 0)
        {
            LastAttempt = new InterruptAttempt(false, false, 0, 0);
            return false;
        }

        var attached = NativeMethods.AttachConsole((uint)processId);
        var attachError = attached ? 0 : Marshal.GetLastWin32Error();

        if (!attached && attachError == 5)
        {
            // 调用方已经有控制台：先脱离，再挂到子进程的控制台。
            NativeMethods.FreeConsole();
            attached = NativeMethods.AttachConsole((uint)processId);
            attachError = attached ? 0 : Marshal.GetLastWin32Error();
        }

        if (!attached)
        {
            LastAttempt = new InterruptAttempt(false, false, attachError, 0);
            return false;
        }

        NativeMethods.SetConsoleCtrlHandler(IgnoreCtrl, add: true);
        try
        {
            // 已挂到子进程的控制台，向该控制台上的所有进程发 Ctrl+C；
            // 我们自己的处理函数返回 true，不会跟着退出。
            var sent = NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, 0);
            var sendError = sent ? 0 : Marshal.GetLastWin32Error();
            LastAttempt = new InterruptAttempt(true, sent, 0, sendError);

            if (sent)
            {
                Thread.Sleep(60);
            }

            return sent;
        }
        finally
        {
            NativeMethods.SetConsoleCtrlHandler(IgnoreCtrl, add: false);
            NativeMethods.FreeConsole();
        }
    }

    // 必须保持静态引用，避免委托被 GC 回收。
    private static readonly NativeMethods.ConsoleCtrlHandler IgnoreCtrl = _ => true;
}
