using System.Runtime.InteropServices;
using Siming.Core.Logging;
using Siming.Core.Processes;

namespace Siming.App;

public sealed class LogForm : Form
{
    private const int WM_SETREDRAW = 0x000B;

    private readonly EntryRuntime _entry;
    private readonly ProcessSupervisor _supervisor;
    private readonly Label _header;
    private readonly TextBox _text;
    private readonly CheckBox _autoScroll;
    private readonly ToolStripStatusLabel _footer;
    private readonly ToolStripButton _restartButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _bottomButton;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly object _pendingGate = new();
    private readonly List<string> _pending = [];
    private int _renderedLines;
    private long _totalLines;
    private long _droppedLines;
    private bool _closing;
    private bool _suppressAutoScroll;
    private bool _ready;

    public LogForm(EntryRuntime entry, ProcessSupervisor supervisor)
    {
        _entry = entry;
        _supervisor = supervisor;

        Text = $"{entry.Config.Name} — 日志";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(780, 540);
        MinimumSize = new Size(520, 340);
        ShowInTaskbar = true;

        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 7, 10, 4),
            AutoEllipsis = true,
            Text = BuildHeaderText(),
        };

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 2, 6, 2),
        };

        _restartButton = Button("重启", async () => await _supervisor.RestartAsync(_entry));
        _stopButton = Button("停止", async () => await _supervisor.StopAsync(_entry));
        var copyButton = Button("复制全部", CopyAll);
        var clearButton = Button("清空", () => _entry.Log.Clear());
        _bottomButton = Button("回到底部", ReturnToBottom);
        _bottomButton.Enabled = false;

        _autoScroll = new CheckBox
        {
            Text = "自动滚动",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(8, 4, 0, 0),
        };
        _autoScroll.CheckedChanged += (_, _) => OnAutoScrollChanged();

        toolbar.Items.Add(_restartButton);
        toolbar.Items.Add(_stopButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(copyButton);
        toolbar.Items.Add(clearButton);
        toolbar.Items.Add(_bottomButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripControlHost(_autoScroll) { AutoSize = true });

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9.5f),
            BackColor = SystemColors.Window,
        };

        _footer = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        var status = new StatusStrip
        {
            SizingGrip = false,
        };
        status.Items.Add(_footer);

        Controls.Add(_text);
        Controls.Add(toolbar);
        Controls.Add(_header);
        Controls.Add(status);

        var snapshot = _entry.Log.Snapshot();
        _text.Text = snapshot.Length == 0 ? "" : string.Join("\r\n", snapshot) + "\r\n";
        _renderedLines = snapshot.Length;
        _totalLines = _entry.Log.TotalLines;
        _droppedLines = _entry.Log.DroppedLines;

        _entry.Log.Appended += OnAppended;
        _entry.Log.Cleared += OnCleared;
        _supervisor.StateChanged += OnStateChanged;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Flush();
        _timer.Start();

        Shown += (_, _) =>
        {
            ScrollToEnd();
            _ready = true;
        };

        UpdateState();
        UpdateFooter();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public EntryRuntime Entry => _entry;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _timer.Stop();
        _timer.Dispose();
        _entry.Log.Appended -= OnAppended;
        _entry.Log.Cleared -= OnCleared;
        _supervisor.StateChanged -= OnStateChanged;
        base.OnFormClosing(e);
    }

    private static ToolStripButton Button(string text, Action action)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void OnAppended(object? sender, LogAppendedEventArgs e)
    {
        lock (_pendingGate)
        {
            _pending.AddRange(e.Lines);
            _totalLines = e.TotalLines;
            _droppedLines = e.DroppedLines;
        }
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        Ui(() =>
        {
            lock (_pendingGate)
            {
                _pending.Clear();
            }

            _text.Clear();
            _renderedLines = 0;
            _totalLines = 0;
            _droppedLines = 0;
            UpdateFooter();
        });
    }

    private void OnStateChanged(object? sender, EntryEventArgs e)
    {
        if (!ReferenceEquals(e.Entry, _entry))
        {
            return;
        }

        Ui(UpdateState);
    }

    private void Flush()
    {
        if (_closing || IsDisposed || !_ready)
        {
            return;
        }

        if (!_autoScroll.Checked)
        {
            lock (_pendingGate)
            {
                _pending.Clear();
            }

            UpdateFooter();
            return;
        }

        if (!IsAtBottom())
        {
            PauseAutoScroll();
            lock (_pendingGate)
            {
                _pending.Clear();
            }

            UpdateFooter();
            return;
        }

        List<string> lines;
        lock (_pendingGate)
        {
            if (_pending.Count == 0)
            {
                UpdateFooter();
                return;
            }

            lines = [.. _pending];
            _pending.Clear();
        }

        if (_renderedLines > _entry.Log.Capacity * 2)
        {
            ResetFromBuffer();
            return;
        }

        SendMessage(_text.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            _text.AppendText(string.Join("\r\n", lines) + "\r\n");
            _renderedLines += lines.Count;
        }
        finally
        {
            SendMessage(_text.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            _text.Invalidate();
        }

        ScrollToEnd();
        UpdateFooter();
    }

    private void ResetFromBuffer()
    {
        lock (_pendingGate)
        {
            _pending.Clear();
        }

        var snapshot = _entry.Log.Snapshot();
        _text.Text = snapshot.Length == 0 ? "" : string.Join("\r\n", snapshot) + "\r\n";
        _renderedLines = snapshot.Length;
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        _text.SelectionStart = _text.TextLength;
        _text.SelectionLength = 0;
        _text.ScrollToCaret();
    }

    private void ReturnToBottom()
    {
        if (!_autoScroll.Checked)
        {
            _autoScroll.Checked = true;
        }

        ResetFromBuffer();
        _bottomButton.Enabled = false;
    }

    private void PauseAutoScroll()
    {
        _suppressAutoScroll = true;
        try
        {
            _autoScroll.Checked = false;
        }
        finally
        {
            _suppressAutoScroll = false;
        }

        _bottomButton.Enabled = true;
    }

    private void OnAutoScrollChanged()
    {
        if (_suppressAutoScroll)
        {
            return;
        }

        _bottomButton.Enabled = !_autoScroll.Checked;
        if (_autoScroll.Checked)
        {
            ResetFromBuffer();
        }

        UpdateFooter();
    }

    private bool IsAtBottom()
    {
        if (!_text.IsHandleCreated)
        {
            return true;
        }

        var info = new SCROLLINFO
        {
            cbSize = Marshal.SizeOf<SCROLLINFO>(),
            fMask = SIF_RANGE | SIF_PAGE | SIF_POS,
        };

        if (!GetScrollInfo(_text.Handle, SB_VERT, ref info))
        {
            return true;
        }

        var bottom = info.nMax - (int)info.nPage + 1;
        return info.nPos >= bottom - 1;
    }

    private void UpdateState()
    {
        _header.Text = BuildHeaderText();
        _restartButton.Enabled = _entry.State == EntryState.Running;
        _stopButton.Enabled = _entry.State is EntryState.Running or EntryState.Starting;
    }

    private string BuildHeaderText()
    {
        var state = _entry.State switch
        {
            EntryState.Running => "运行中",
            EntryState.Starting => "启动中",
            EntryState.Stopping => "停止中",
            _ => "已停止",
        };

        var uptime = _entry.Uptime is { } value ? $" · 已运行 {MainForm.FormatDuration(value)}" : "";
        return $"{_entry.Config.Name} · {state}{uptime} · {_entry.Config.Command}    {_entry.Config.Cwd}";
    }

    private void UpdateFooter()
    {
        var paused = _autoScroll.Checked ? "" : "已暂停 · ";
        _footer.Text = $"{paused}最近 {_entry.Log.Capacity} 行 · 累计 {_totalLines} 行 · 已丢弃 {_droppedLines} 行";
    }

    private void CopyAll()
    {
        try
        {
            Clipboard.SetText(_text.Text);
            _footer.Text = "已复制全部日志";
        }
        catch (Exception ex)
        {
            _footer.Text = $"复制失败：{ex.Message}";
        }
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }
        else
        {
            action();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int SB_VERT = 1;
    private const uint SIF_RANGE = 0x0001;
    private const uint SIF_PAGE = 0x0002;
    private const uint SIF_POS = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public int cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO si);
}
