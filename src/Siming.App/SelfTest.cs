using System.Diagnostics;
using System.Text;
using Siming.Core.Configuration;
using Siming.Core.Processes;

namespace Siming.App;

/// <summary>
/// --selftest：在无控制台的 WinExe 环境里验证「启动 → Ctrl+Break → 整树终止」。
/// 结果写到 %TEMP%\siming-selftest.log，便于命令行核对。
/// </summary>
internal static class SelfTest
{
    public static async Task<bool> RunAsync()
    {
        var log = new StringBuilder();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "siming-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var baseline = Process.GetProcessesByName("PING")
            .Select(process => process.Id)
            .ToHashSet();

        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Name = "selftest",
                    Cwd = directory,
                    Command = "ping -t 127.0.0.1",
                    Shell = "cmd",
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(config);
        var entry = supervisor.Snapshot()[0];

        try
        {
            await supervisor.StartAsync(entry);
            log.AppendLine($"start: state={entry.State} pid={entry.Pid}");

            var rootPid = entry.Pid;
            var childPid = await WaitForNewProcessAsync("PING", baseline, TimeSpan.FromSeconds(8));
            log.AppendLine($"child: pid={childPid}");

            var stopwatch = Stopwatch.StartNew();
            await supervisor.StopAsync(entry);
            stopwatch.Stop();

            var rootAlive = rootPid is { } root && IsAlive(root);
            var childAlive = childPid is { } child && IsAlive(child);
            var graceful = stopwatch.ElapsedMilliseconds < 4000;

            log.AppendLine($"stop: {stopwatch.ElapsedMilliseconds}ms graceful={graceful} state={entry.State}");
            var attempt = ConsoleInterrupt.LastAttempt;
            log.AppendLine(
                $"interrupt: attached={attempt.Attached} sent={attempt.Sent} "
                + $"attachError={attempt.AttachError} sendError={attempt.SendError}");
            log.AppendLine($"leftovers: root={rootAlive} child={childAlive}");

            var passed = entry.State == EntryState.Stopped
                && rootPid is not null
                && childPid is not null
                && !rootAlive
                && !childAlive;

            log.AppendLine($"result: {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        catch (Exception ex)
        {
            log.AppendLine($"exception: {ex}");
            return false;
        }
        finally
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "siming-selftest.log"),
                log.ToString());

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
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
}
