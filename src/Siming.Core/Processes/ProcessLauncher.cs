using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Siming.Core.Configuration;

namespace Siming.Core.Processes;

/// <summary>
/// 一次成功启动的进程：进程句柄、Job、输出流。Dispose 会结束 Job 中残留的子进程。
/// </summary>
public sealed class LaunchedProcess : IDisposable
{
    private bool _disposed;

    internal LaunchedProcess(
        SafeProcessHandle process,
        JobObject job,
        int pid,
        StreamReader standardOutput,
        StreamReader standardError)
    {
        Process = process;
        Job = job;
        Pid = pid;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public SafeProcessHandle Process { get; }

    public JobObject Job { get; }

    public int Pid { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StandardOutput.Dispose();
        StandardError.Dispose();
        Job.Dispose();
        Process.Dispose();
    }
}

public static class ProcessLauncher
{
    public static LaunchedProcess Launch(EntryConfig entry)
    {
        var (application, commandLine) = CommandLineBuilder.Build(entry);
        var encoding = EncodingNames.Resolve(entry.Encoding);

        var security = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        SafeFileHandle? stdoutRead = null;
        SafeFileHandle? stdoutWrite = null;
        SafeFileHandle? stderrRead = null;
        SafeFileHandle? stderrWrite = null;
        SafeFileHandle? nul = null;
        JobObject? job = null;
        SafeProcessHandle? process = null;
        var environment = IntPtr.Zero;
        var ownsEnvironment = false;

        try
        {
            if (!NativeMethods.CreatePipe(out stdoutRead, out stdoutWrite, ref security, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 stdout 管道失败");
            }

            if (!NativeMethods.CreatePipe(out stderrRead, out stderrWrite, ref security, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 stderr 管道失败");
            }

            NativeMethods.SetHandleInformation(stdoutRead, NativeMethods.HANDLE_FLAG_INHERIT, 0);
            NativeMethods.SetHandleInformation(stderrRead, NativeMethods.HANDLE_FLAG_INHERIT, 0);

            nul = NativeMethods.CreateFileW(
                "NUL",
                NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                ref security,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (nul.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "打开 NUL 设备失败");
            }

            job = JobObject.Create(killOnClose: true);
            environment = EnvironmentBlock.Build(entry.Env, out ownsEnvironment);

            var startup = new NativeMethods.STARTUPINFO
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                dwFlags = NativeMethods.STARTF_USESHOWWINDOW | NativeMethods.STARTF_USESTDHANDLES,
                wShowWindow = NativeMethods.SW_HIDE,
                hStdInput = nul.DangerousGetHandle(),
                hStdOutput = stdoutWrite.DangerousGetHandle(),
                hStdError = stderrWrite.DangerousGetHandle(),
            };

            const uint flags = NativeMethods.CREATE_SUSPENDED
                | NativeMethods.CREATE_NEW_CONSOLE
                | NativeMethods.CREATE_NEW_PROCESS_GROUP
                | NativeMethods.CREATE_UNICODE_ENVIRONMENT;

            if (!NativeMethods.CreateProcessW(
                    application,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: true,
                    flags,
                    environment,
                    entry.Cwd,
                    ref startup,
                    out var info))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"创建进程失败（shell={entry.Shell}）：{commandLine}");
            }

            // 父进程侧不再需要写入端与 NUL。
            stdoutWrite.Dispose();
            stdoutWrite = null;
            stderrWrite.Dispose();
            stderrWrite = null;
            nul.Dispose();
            nul = null;

            process = new SafeProcessHandle(info.hProcess, ownsHandle: true);
            try
            {
                // 先挂 Job 再恢复线程，保证子进程从第一条指令起就在 Job 内。
                job.Assign(process);
                if (NativeMethods.ResumeThread(info.hThread) == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "恢复进程主线程失败");
                }
            }
            catch
            {
                // 挂 Job 或恢复线程失败时，进程还停在 CREATE_SUSPENDED，必须主动结束。
                NativeMethods.TerminateProcess(process, 1);
                throw;
            }
            finally
            {
                NativeMethods.CloseHandle(info.hThread);
            }

            StreamReader stdout;
            StreamReader stderr;
            try
            {
                stdout = new StreamReader(
                    new FileStream(stdoutRead, FileAccess.Read, 4096, isAsync: false),
                    encoding,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096);
                stderr = new StreamReader(
                    new FileStream(stderrRead, FileAccess.Read, 4096, isAsync: false),
                    encoding,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096);
            }
            catch
            {
                stdoutRead?.Dispose();
                stderrRead?.Dispose();
                throw;
            }

            stdoutRead = null;
            stderrRead = null;

            var launched = new LaunchedProcess(process, job, info.dwProcessId, stdout, stderr);
            process = null;
            job = null;
            return launched;
        }
        catch
        {
            stdoutRead?.Dispose();
            stderrRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrWrite?.Dispose();
            nul?.Dispose();
            process?.Dispose();
            job?.Dispose();
            throw;
        }
        finally
        {
            if (ownsEnvironment && environment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environment);
            }
        }
    }
}
