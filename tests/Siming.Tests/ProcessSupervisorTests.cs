using System.Diagnostics;
using Siming.Core.Configuration;
using Siming.Core.Processes;

namespace Siming.Tests;

public sealed class ProcessSupervisorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "siming-tests",
        Guid.NewGuid().ToString("N"));

    public ProcessSupervisorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task Echo_Exits_And_Captures_Output()
    {
        using var supervisor = CreateSupervisor("echo hello");
        var entry = supervisor.Snapshot()[0];

        await supervisor.StartAsync(entry);
        await WaitUntilAsync(() => entry.State == EntryState.Stopped, TimeSpan.FromSeconds(10));

        Assert.Contains(entry.Log.Snapshot(), line => line.Contains("hello", StringComparison.Ordinal));
        Assert.Equal(0, entry.LastExitCode);
        Assert.Null(entry.LastError);
    }

    [Fact]
    public async Task Stop_Terminates_Whole_Process_Tree()
    {
        var baselinePings = Process.GetProcessesByName("PING")
            .Select(process => process.Id)
            .ToHashSet();

        using var supervisor = CreateSupervisor("ping -t 127.0.0.1");
        var entry = supervisor.Snapshot()[0];

        await supervisor.StartAsync(entry);
        Assert.Equal(EntryState.Running, entry.State);

        var rootPid = entry.Pid;
        Assert.NotNull(rootPid);

        var childPid = await WaitForNewProcessAsync("PING", baselinePings, TimeSpan.FromSeconds(10));
        Assert.NotNull(childPid);

        await supervisor.StopAsync(entry);

        Assert.Equal(EntryState.Stopped, entry.State);
        Assert.False(IsAlive(rootPid!.Value), $"cmd {rootPid} 仍在运行");
        Assert.False(IsAlive(childPid!.Value), $"ping {childPid} 仍在运行");
    }

    [Fact]
    public async Task Restart_Starts_New_Process()
    {
        using var supervisor = CreateSupervisor("ping -t 127.0.0.1");
        var entry = supervisor.Snapshot()[0];

        await supervisor.StartAsync(entry);
        var firstPid = entry.Pid;
        Assert.NotNull(firstPid);

        await supervisor.RestartAsync(entry);

        Assert.Equal(EntryState.Running, entry.State);
        Assert.NotNull(entry.Pid);
        Assert.NotEqual(firstPid, entry.Pid);

        await supervisor.StopAsync(entry);
        Assert.False(IsAlive(firstPid!.Value));
    }

    [Fact]
    public async Task Start_Reports_Missing_Directory()
    {
        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Name = "bad",
                    Cwd = Path.Combine(_directory, "does-not-exist"),
                    Command = "echo 1",
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(config);
        var entry = supervisor.Snapshot()[0];

        await supervisor.StartAsync(entry);

        Assert.Equal(EntryState.Stopped, entry.State);
        Assert.Contains("工作目录不存在", entry.LastError);
        Assert.Contains(entry.Log.Snapshot(), line => line.Contains("启动失败"));
    }

    [Fact]
    public async Task ApplyConfig_WithStableId_KeepsRunningRuntime_OnRename()
    {
        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Id = "stable",
                    Name = "old",
                    Cwd = _directory,
                    Command = "ping -t 127.0.0.1",
                    Shell = "cmd",
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(
            config,
            gracefulStopTimeout: TimeSpan.FromMilliseconds(400),
            forceStopTimeout: TimeSpan.FromSeconds(2));

        var entry = supervisor.Snapshot()[0];
        await supervisor.StartAsync(entry);
        var pid = entry.Pid;
        Assert.NotNull(pid);

        config.Entries[0].Name = "new";
        supervisor.ApplyConfig(config);

        var snapshot = supervisor.Snapshot();
        Assert.Single(snapshot);
        Assert.Same(entry, snapshot[0]);
        Assert.Equal("new", entry.Config.Name);
        Assert.Equal(EntryState.Running, entry.State);
        Assert.Equal(pid, entry.Pid);

        await supervisor.StopAsync(entry);
    }

    [Fact]
    public void ApplyConfig_Reorders_Entries_ByConfigOrder()
    {
        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig { Id = "a", Name = "A", Cwd = _directory, Command = "echo 1" },
                new EntryConfig { Id = "b", Name = "B", Cwd = _directory, Command = "echo 2" },
            ],
        };

        using var supervisor = new ProcessSupervisor(config);
        Assert.Equal(["a", "b"], supervisor.Snapshot().Select(entry => entry.Config.Key));

        config.Entries.Reverse();
        supervisor.ApplyConfig(config);

        Assert.Equal(["b", "a"], supervisor.Snapshot().Select(entry => entry.Config.Key));
    }

    private ProcessSupervisor CreateSupervisor(string command) => new(
        new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Name = "test",
                    Cwd = _directory,
                    Command = command,
                    Shell = "cmd",
                    Encoding = "utf-8",
                },
            ],
        },
        gracefulStopTimeout: TimeSpan.FromMilliseconds(400),
        forceStopTimeout: TimeSpan.FromSeconds(2));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"等待超时（{timeout.TotalSeconds:0.#}s）");
    }

    private static async Task<int?> WaitForNewProcessAsync(
        string processName,
        HashSet<int> baseline,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var candidate = Process.GetProcessesByName(processName)
                .Select(process => process.Id)
                .FirstOrDefault(id => !baseline.Contains(id));

            if (candidate != 0)
            {
                return candidate;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var process in Process.GetProcessesByName("PING"))
        {
            try
            {
                if (process.StartTime > DateTime.Now.AddMinutes(-2))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
