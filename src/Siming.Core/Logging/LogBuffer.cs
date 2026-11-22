namespace Siming.Core.Logging;

public sealed class LogAppendedEventArgs(IReadOnlyList<string> lines, long totalLines, long droppedLines) : EventArgs
{
    public IReadOnlyList<string> Lines { get; } = lines;

    public long TotalLines { get; } = totalLines;

    public long DroppedLines { get; } = droppedLines;
}

/// <summary>
/// 定长环形日志缓冲：只保留最近 N 行，接收与读取都不阻塞。
/// </summary>
public sealed class LogBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines;
    private int _capacity;

    public LogBuffer(int capacity)
    {
        _capacity = Math.Clamp(capacity, 1, 100_000);
        _lines = new Queue<string>(_capacity);
    }

    public int Capacity => Volatile.Read(ref _capacity);

    public long TotalLines { get; private set; }

    public long DroppedLines { get; private set; }

    public event EventHandler<LogAppendedEventArgs>? Appended;

    public event EventHandler? Cleared;

    public void Add(string line)
    {
        AddRange([line]);
    }

    public void AddRange(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var stripped = new List<string>(lines.Count);
        long total;
        long dropped;
        lock (_gate)
        {
            foreach (var raw in lines)
            {
                var line = Ansi.Strip(raw);
                stripped.Add(line);
                _lines.Enqueue(line);
                TotalLines++;
                while (_lines.Count > _capacity)
                {
                    _lines.Dequeue();
                    DroppedLines++;
                }
            }

            total = TotalLines;
            dropped = DroppedLines;
        }

        Appended?.Invoke(this, new LogAppendedEventArgs(stripped, total, dropped));
    }

    public string[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _lines];
        }
    }

    public void Resize(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, 100_000);
        lock (_gate)
        {
            _capacity = capacity;
            while (_lines.Count > _capacity)
            {
                _lines.Dequeue();
                DroppedLines++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            TotalLines = 0;
            DroppedLines = 0;
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
