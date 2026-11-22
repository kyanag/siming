using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Siming.Core.Processes;

/// <summary>
/// 一个条目对应一个 Job Object：停止时整树终止，进程崩溃时由系统兜底。
/// </summary>
public sealed class JobObject : IDisposable
{
    private JobObject(SafeJobHandle handle, bool killOnClose)
    {
        Handle = handle;
        KillOnClose = killOnClose;
    }

    internal SafeJobHandle Handle { get; }

    public bool KillOnClose { get; private set; }

    public static JobObject Create(bool killOnClose = true)
    {
        var handle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 Job Object 失败");
        }

        var job = new JobObject(handle, killOnClose);
        if (!job.SetKillOnClose(killOnClose))
        {
            job.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "设置 Job Object 限制失败");
        }

        return job;
    }

    public void Assign(SafeProcessHandle process)
    {
        if (!NativeMethods.AssignProcessToJobObject(Handle, process))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"将进程加入 Job Object 失败（错误 {error}）");
        }
    }

    public bool Terminate(uint exitCode = 1)
    {
        return NativeMethods.TerminateJobObject(Handle, exitCode);
    }

    /// <summary>
    /// 退出时选择「保留进程」：清除 kill-on-close，再关闭句柄，子进程继续运行。
    /// 清除失败返回 false，调用方应降级为结束进程。
    /// </summary>
    public bool ClearKillOnClose()
    {
        if (!SetKillOnClose(false))
        {
            return false;
        }

        KillOnClose = false;
        return true;
    }

    public void Dispose()
    {
        Handle.Dispose();
    }

    private bool SetKillOnClose(bool enabled)
    {
        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = enabled
            ? NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            : 0;

        return NativeMethods.SetInformationJobObject(
            Handle,
            NativeMethods.JobObjectExtendedLimitInformation,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
    }
}
