using Siming.Core.Configuration;

namespace Siming.Core.Processes;

public sealed class EntryEventArgs(EntryRuntime entry) : EventArgs
{
    public EntryRuntime Entry { get; } = entry;
}

/// <summary>
/// 管理所有条目的启动、停止、重启。状态变更在后台线程触发，UI 需自行切回主线程。
/// </summary>
public sealed class ProcessSupervisor : IDisposable
{
    private readonly object _gate = new();
    private readonly List<EntryRuntime> _entries = [];
    private readonly TimeSpan _gracefulTimeout;
    private readonly TimeSpan _forceTimeout;
    private bool _disposed;

    public ProcessSupervisor(
        AppConfig config,
        TimeSpan? gracefulStopTimeout = null,
        TimeSpan? forceStopTimeout = null)
    {
        _gracefulTimeout = gracefulStopTimeout ?? TimeSpan.FromSeconds(5);
        _forceTimeout = forceStopTimeout ?? TimeSpan.FromSeconds(3);

        var logLines = config.Settings?.EffectiveLogLines ?? 200;
        foreach (var entry in config.Entries)
        {
            _entries.Add(new EntryRuntime(entry, logLines));
        }
    }

    public event EventHandler<EntryEventArgs>? StateChanged;

    public event EventHandler? EntriesChanged;

    public EntryRuntime[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }

    public EntryRuntime? Find(string key)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(entry =>
                string.Equals(entry.Config.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task StartAsync(EntryRuntime entry)
    {
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartCoreAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task StopAsync(EntryRuntime entry)
    {
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task RestartAsync(EntryRuntime entry)
    {
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(entry).ConfigureAwait(false);
            await StartCoreAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task StartAllAsync()
    {
        foreach (var entry in Snapshot())
        {
            if (entry.State == EntryState.Stopped)
            {
                await StartAsync(entry).ConfigureAwait(false);
            }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var entry in Snapshot())
        {
            if (entry.State != EntryState.Stopped)
            {
                await StopAsync(entry).ConfigureAwait(false);
            }
        }
    }

    public async Task StartAutoStartAsync()
    {
        foreach (var entry in Snapshot())
        {
            if (entry.Config.AutoStart && entry.State == EntryState.Stopped)
            {
                await StartAsync(entry).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 热重载：更新已停止条目的配置、增删条目；运行中的条目保留进程，仅更新显示配置。
    /// 配置里被删掉的运行中条目会标记为 IsOrphan，直到停止。
    /// </summary>
    public void ApplyConfig(AppConfig config)
    {
        var logLines = config.Settings?.EffectiveLogLines ?? 200;

        lock (_gate)
        {
            var incoming = new Dictionary<string, EntryConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in config.Entries)
            {
                incoming.TryAdd(entry.Key, entry);
            }

            foreach (var existing in _entries.ToList())
            {
                if (incoming.TryGetValue(existing.Config.Key, out var next))
                {
                    existing.Config = next;
                    existing.IsOrphan = false;
                    incoming.Remove(next.Key);
                }
                else if (existing.State == EntryState.Stopped)
                {
                    _entries.Remove(existing);
                }
                else
                {
                    existing.IsOrphan = true;
                }
            }

            foreach (var next in incoming.Values)
            {
                _entries.Add(new EntryRuntime(next, logLines));
            }

            foreach (var entry in _entries)
            {
                entry.Log.Resize(logLines);
            }

            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < config.Entries.Count; index++)
            {
                order.TryAdd(config.Entries[index].Key, index);
            }

            _entries.Sort((left, right) =>
            {
                var leftOrder = order.TryGetValue(left.Config.Key, out var leftIndex)
                    ? leftIndex
                    : int.MaxValue;
                var rightOrder = order.TryGetValue(right.Config.Key, out var rightIndex)
                    ? rightIndex
                    : int.MaxValue;

                return leftOrder != rightOrder
                    ? leftOrder.CompareTo(rightOrder)
                    : string.Compare(left.Config.Name, right.Config.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 退出软件。keepRunning = true 时清除 Job 的 kill-on-close，让子进程脱离管理继续运行。
    /// </summary>
    public void Shutdown(bool keepRunning)
    {
        EntryRuntime[] snapshot;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            snapshot = [.. _entries];
        }

        foreach (var entry in snapshot)
        {
            var launched = entry.Launched;
            if (launched is null)
            {
                continue;
            }

            try
            {
                if (keepRunning)
                {
                    if (!launched.Job.ClearKillOnClose())
                    {
                        launched.Job.Terminate(1);
                    }
                }
                else
                {
                    launched.Job.Terminate(1);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                launched.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            entry.Launched = null;
            entry.Pid = null;
            entry.State = EntryState.Stopped;
        }

        foreach (var entry in snapshot)
        {
            entry.Gate.Dispose();
        }
    }

    public void Dispose() => Shutdown(keepRunning: false);

    private async Task StartCoreAsync(EntryRuntime entry)
    {
        if (_disposed || entry.State != EntryState.Stopped)
        {
            return;
        }

        var validationError = EntryValidator.ValidateForRun(entry.Config);
        if (validationError is not null)
        {
            Fail(entry, validationError);
            return;
        }

        entry.LastError = null;
        entry.State = EntryState.Starting;
        RaiseState(entry);

        LaunchedProcess launched;
        try
        {
            launched = ProcessLauncher.Launch(entry.Config);
        }
        catch (Exception ex)
        {
            Fail(entry, ex.Message);
            return;
        }

        var signal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Launched = launched;
        entry.ExitSignal = signal;
        entry.Pid = launched.Pid;
        entry.StartedAt = DateTimeOffset.Now;
        entry.LastExitCode = null;
        entry.LastExitedAt = null;
        entry.State = EntryState.Running;
        RaiseState(entry);

        _ = Task.Run(() => WatchAsync(entry, launched, signal));
    }

    private async Task StopCoreAsync(EntryRuntime entry)
    {
        if (entry.State == EntryState.Stopped)
        {
            return;
        }

        var launched = entry.Launched;
        var signal = entry.ExitSignal;
        if (launched is null || signal is null)
        {
            entry.State = EntryState.Stopped;
            entry.Pid = null;
            RaiseState(entry);
            return;
        }

        entry.State = EntryState.Stopping;
        RaiseState(entry);

        ConsoleInterrupt.TrySendCtrlBreak(launched.Pid);

        var completed = await Task.WhenAny(signal.Task, Task.Delay(_gracefulTimeout)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, signal.Task))
        {
            try
            {
                launched.Job.Terminate(1);
            }
            catch (ObjectDisposedException)
            {
            }

            await Task.WhenAny(signal.Task, Task.Delay(_forceTimeout)).ConfigureAwait(false);
        }

        // 正常情况下 watcher 已经把状态置回 Stopped；这里只兜底。
        if (entry.State != EntryState.Stopped)
        {
            entry.State = EntryState.Stopped;
            entry.Pid = null;
            entry.LastExitedAt = DateTimeOffset.Now;
            RaiseState(entry);
        }
    }

    private async Task WatchAsync(EntryRuntime entry, LaunchedProcess launched, TaskCompletionSource<int> signal)
    {
        var stdout = PumpAsync(entry, launched.StandardOutput);
        var stderr = PumpAsync(entry, launched.StandardError);

        var exitCode = await Task.Run(() => WaitForExit(launched)).ConfigureAwait(false);

        // 给输出一点时间落盘，但不无限等。
        await Task.WhenAny(
            Task.WhenAll(stdout, stderr),
            Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

        try
        {
            launched.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        if (ReferenceEquals(entry.Launched, launched))
        {
            entry.Launched = null;
            entry.Pid = null;
            entry.LastExitCode = exitCode;
            entry.LastExitedAt = DateTimeOffset.Now;
            entry.State = EntryState.Stopped;
            RaiseState(entry);
        }

        signal.TrySetResult(exitCode);
    }

    private static async Task PumpAsync(EntryRuntime entry, StreamReader reader)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                entry.Log.Add(line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static int WaitForExit(LaunchedProcess launched)
    {
        NativeMethods.WaitForSingleObject(launched.Process, NativeMethods.INFINITE);
        return NativeMethods.GetExitCodeProcess(launched.Process, out var code)
            ? unchecked((int)code)
            : -1;
    }

    private void Fail(EntryRuntime entry, string message)
    {
        entry.LastError = message;
        entry.LastExitCode = null;
        entry.State = EntryState.Stopped;
        entry.Pid = null;
        entry.Log.Add($"[司命] 启动失败：{message}");
        RaiseState(entry);
    }

    private void RaiseState(EntryRuntime entry)
    {
        StateChanged?.Invoke(this, new EntryEventArgs(entry));
    }
}
