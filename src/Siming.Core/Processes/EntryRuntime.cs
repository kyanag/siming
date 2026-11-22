using Siming.Core.Configuration;
using Siming.Core.Extraction;
using Siming.Core.Logging;

namespace Siming.Core.Processes;

/// <summary>
/// 一个条目的运行时视图：配置、状态、日志、提取值。UI 只读，写操作走 ProcessSupervisor。
/// </summary>
public sealed class EntryRuntime
{
    private readonly LogExtractor _extractor;

    internal EntryRuntime(EntryConfig config, int logLines)
    {
        Config = config;
        Log = new LogBuffer(logLines);
        Gate = new SemaphoreSlim(1, 1);
        _extractor = new LogExtractor(config.Extract);
        Log.Appended += OnLogAppended;
    }

    public EntryConfig Config { get; internal set; }

    public LogBuffer Log { get; }

    /// <summary>从日志里提取的字段。值只在运行期有效，启动与停止时清空。</summary>
    public LogExtractor Extractor => _extractor;

    public EntryState State { get; internal set; }

    public int? Pid { get; internal set; }

    public DateTimeOffset? StartedAt { get; internal set; }

    public DateTimeOffset? LastExitedAt { get; internal set; }

    public int? LastExitCode { get; internal set; }

    public string? LastError { get; internal set; }

    /// <summary>配置里已删除，但进程仍在运行。</summary>
    public bool IsOrphan { get; internal set; }

    public bool IsRunning => State is EntryState.Running or EntryState.Starting;

    public TimeSpan? Uptime => State == EntryState.Running && StartedAt is { } started
        ? DateTimeOffset.Now - started
        : null;

    internal SemaphoreSlim Gate { get; }

    internal LaunchedProcess? Launched { get; set; }

    internal TaskCompletionSource<int>? ExitSignal { get; set; }

    internal void ReconfigureExtractor() => _extractor.Reconfigure(Config.Extract);

    private void OnLogAppended(object? sender, LogAppendedEventArgs args)
    {
        // 在输出线程上同步执行：只做逐行正则，不做 UI、不做 IO。
        foreach (var line in args.Lines)
        {
            _extractor.OnLine(line);
        }
    }
}
