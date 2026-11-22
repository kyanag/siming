using System.Diagnostics;
using Siming.Core.Configuration;
using Siming.Core.Control;
using Siming.Core.Platform;
using Siming.Core.Processes;

namespace Siming.App;

public sealed class MainForm : Form
{
    private static readonly Color RunningColor = Color.FromArgb(0x2E, 0x9E, 0x44);
    private static readonly Color StartingColor = Color.FromArgb(0xD9, 0xA4, 0x00);
    private static readonly Color StoppingColor = Color.FromArgb(0xC2, 0x6A, 0x00);
    private static readonly Color StoppedColor = Color.FromArgb(0x8A, 0x8A, 0x8A);
    private static readonly Color ErrorColor = Color.FromArgb(0xC0, 0x39, 0x2B);

    private readonly ConfigStore _store;
    private readonly ProcessSupervisor _supervisor;
    private readonly ListView _list;
    private readonly Label _emptyLabel;
    private readonly ImageList _statusImages;
    private readonly ToolStrip _toolbar;
    private readonly ToolStripButton _startButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _restartButton;
    private readonly ToolStripButton _logButton;
    private readonly ToolStripButton _addButton;
    private readonly ToolStripButton _editButton;
    private readonly ToolStripButton _deleteButton;
    private readonly ToolStripButton _upButton;
    private readonly ToolStripButton _downButton;
    private readonly ToolStripButton _startupButton;
    private readonly StatusStrip _statusBar;
    private readonly ToolStripStatusLabel _countLabel;
    private readonly ToolStripStatusLabel _messageLabel;
    private readonly ToolStripStatusLabel _pathLabel;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _trayMenu;
    private readonly System.Windows.Forms.Timer _uptimeTimer;
    private readonly Dictionary<EntryRuntime, ListViewItem> _items = [];
    private readonly Dictionary<string, LogForm> _logForms = new(StringComparer.OrdinalIgnoreCase);

    private static readonly int[] FixedListColumnLogicalWidths = [190, 110, 80, 76, 280];

    private const int ColumnScrollbarReserveLogical = 20;

    private GeneratedIcon? _idleIcon;
    private GeneratedIcon? _runningIcon;
    private ControlServer? _controlServer;
    private bool _exiting;
    private bool _hideHintShown;
    private bool _syncingStartup;

    public MainForm(ConfigStore store, ProcessSupervisor supervisor)
    {
        _store = store;
        _supervisor = supervisor;

        Icon = AppIcon.Shared;
        Text = "司命 · 进程管理器";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 560);
        MinimumSize = new Size(760, 420);
        KeyPreview = true;

        _statusImages = StatusImages.Create();

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            BorderStyle = BorderStyle.None,
            SmallImageList = _statusImages,
        };
        _list.Columns.Add("名称", 190);
        _list.Columns.Add("状态", 110);
        _list.Columns.Add("运行时长", 80);
        _list.Columns.Add("自动启动", 76);
        _list.Columns.Add("命令", 280);
        _list.Columns.Add("工作目录", 320);

        _emptyLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "还没有进程条目\r\n\r\n编辑 exe 同目录的 processes.json 添加条目，保存后会自动重载。",
            ForeColor = Color.FromArgb(0x6A, 0x6A, 0x6A),
            BackColor = SystemColors.Window,
            Visible = false,
        };

        var listHost = new Panel { Dock = DockStyle.Fill };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_emptyLabel);
        _emptyLabel.BringToFront();

        _startButton = ToolbarButton("启动", () => _ = StartSelectedAsync());
        _stopButton = ToolbarButton("停止", () => _ = StopSelectedAsync());
        _restartButton = ToolbarButton("重启", () => _ = RestartSelectedAsync());
        var logButton = ToolbarButton("日志", () => { if (Selected is { } entry) OpenLog(entry); });
        _logButton = logButton;
        _addButton = ToolbarButton("新增", AddEntry);
        _editButton = ToolbarButton("编辑", () => _ = EditSelectedAsync());
        _deleteButton = ToolbarButton("删除", () => _ = DeleteSelectedAsync());
        _upButton = ToolbarButton("↑", () => MoveSelected(-1));
        _downButton = ToolbarButton("↓", () => MoveSelected(1));
        var reloadButton = ToolbarButton("重载配置", () => _store.Reload());
        var openConfigButton = ToolbarButton("打开配置", OpenConfigFile);

        _startupButton = new ToolStripButton("开机自启")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false,
            CheckOnClick = true,
            Checked = _store.Current.Settings.StartWithWindows,
        };
        _startupButton.CheckedChanged += (_, _) =>
        {
            if (!_syncingStartup)
            {
                ApplyStartup(_startupButton.Checked);
            }
        };

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 2, 6, 2),
        };
        _toolbar.Items.AddRange(
        [
            _startButton,
            _stopButton,
            _restartButton,
            _logButton,
            new ToolStripSeparator(),
            _addButton,
            _editButton,
            _deleteButton,
            _upButton,
            _downButton,
            new ToolStripSeparator(),
            reloadButton,
            openConfigButton,
            new ToolStripSeparator(),
            _startupButton,
        ]);

        _countLabel = new ToolStripStatusLabel
        {
            AutoSize = false,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _messageLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _pathLabel = new ToolStripStatusLabel
        {
            IsLink = true,
            Text = Path.GetFileName(_store.FilePath),
            ToolTipText = _store.FilePath,
        };
        _pathLabel.Click += (_, _) => OpenConfigFolder(selectFile: true);

        _statusBar = new StatusStrip { SizingGrip = false };
        _statusBar.Items.AddRange([_countLabel, _messageLabel, _pathLabel]);

        Controls.Add(listHost);
        Controls.Add(_toolbar);
        Controls.Add(_statusBar);

        _trayMenu = new ContextMenuStrip { ShowImageMargin = false, ShowItemToolTips = true };
        _trayMenu.Opening += (_, _) => BuildTrayMenu();

        _tray = new NotifyIcon
        {
            Text = "司命",
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        };

        _supervisor.StateChanged += (_, e) => Ui(() => OnEntryStateChanged(e.Entry));
        _supervisor.EntriesChanged += (_, _) => Ui(RefreshList);
        _store.Changed += (_, e) => Ui(() => OnConfigChanged(e.Result));

        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.SizeChanged += (_, _) => ApplyListColumnWidths(DeviceDpi);
        _list.DoubleClick += (_, _) =>
        {
            if (Selected is { } entry)
            {
                OpenLog(entry);
            }
        };
        _list.KeyDown += OnListKeyDown;

        _uptimeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uptimeTimer.Tick += (_, _) => UpdateRuntimeCells();
        _uptimeTimer.Start();

        RefreshList();
        UpdateTrayIcon();

        // 必须在控件和默认尺寸都设置好之后再开启缩放：
        // WinForms 会在 AutoScaleMode 变化时立即按当前 DPI 执行一次 AutoScale。
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public void ShowFromTray()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        BringToFront();
    }

    /// <summary>在消息循环启动、句柄创建后调用，用于对开机自启做一次校正并拉起 autoStart 条目。</summary>
    public void BeginAutoStart()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            _ = Handle;
        }

        StartControlServer();
        ReportExtractError();

        try
        {
            BeginInvoke(async () =>
            {
                ReconcileStartupSetting();
                await _supervisor.StartAutoStartAsync();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void StartControlServer()
    {
        if (_controlServer is not null)
        {
            return;
        }

        try
        {
            var token = McpToken.Ensure(_store.FilePath);
            var server = new ControlServer(McpToken.ControlPort, token, HandleControlAsync);
            if (!server.Start(out var error))
            {
                server.Dispose();
                SetStatus($"MCP 控制接口启动失败：{error}", error: true);
                return;
            }

            _controlServer = server;
        }
        catch (Exception ex)
        {
            SetStatus($"MCP 控制接口启动失败：{ex.Message}", error: true);
        }
    }

    private async Task<object?> HandleControlAsync(ControlRequest request)
    {
        switch (request.Cmd.Trim().ToLowerInvariant())
        {
            case "ping":
                return new
                {
                    app = "Siming",
                    pid = Environment.ProcessId,
                    version = typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                };

            case "list":
                return UiInvoke(() => new
                {
                    appRunning = true,
                    controlConnected = true,
                    entries = _supervisor.Snapshot().Select(ToStatus).ToArray(),
                });

            case "logs":
                return UiInvoke(() => GetLogs(request.Key, request.Lines ?? 200));

            case "add":
                return UiInvoke(() => new
                {
                    entry = AddEntryFromControl(
                        request.Entry ?? throw new InvalidOperationException("缺少 entry 参数")),
                });

            case "update":
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    throw new InvalidOperationException("缺少 key 参数");
                }

                return UiInvoke(() => new
                {
                    entry = UpdateEntryFromControl(
                        request.Key!,
                        request.Entry ?? throw new InvalidOperationException("缺少 entry 参数")),
                });

            case "remove":
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    throw new InvalidOperationException("缺少 key 参数");
                }

                return await RemoveFromControlAsync(request.Key!, request.Stop ?? true).ConfigureAwait(false);

            case "start":
            case "stop":
            case "restart":
                return await ControlEntryAsync(
                    request.Cmd.Trim().ToLowerInvariant(),
                    request.Key).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"未知命令：{request.Cmd}");
        }
    }

    private async Task<object?> ControlEntryAsync(string command, string? key)
    {
        var runtime = FindRuntime(key) ?? throw new InvalidOperationException($"条目不存在：{key}");

        switch (command)
        {
            case "start":
                await _supervisor.StartAsync(runtime).ConfigureAwait(false);
                break;
            case "stop":
                await _supervisor.StopAsync(runtime).ConfigureAwait(false);
                break;
            case "restart":
                await _supervisor.RestartAsync(runtime).ConfigureAwait(false);
                break;
        }

        var status = UiInvoke(() => ToStatus(runtime));
        return new { entry = status, error = runtime.LastError };
    }

    private async Task<object?> RemoveFromControlAsync(string key, bool stop)
    {
        var runtime = FindRuntime(key);
        if (runtime is null)
        {
            throw new InvalidOperationException($"条目不存在：{key}");
        }

        if (stop && runtime.State != EntryState.Stopped)
        {
            await _supervisor.StopAsync(runtime).ConfigureAwait(false);
            if (runtime.State != EntryState.Stopped)
            {
                throw new InvalidOperationException("停止失败，未删除");
            }
        }

        return UiInvoke(() => RemoveEntryFromControl(runtime.Config.Key));
    }

    private EntryStatus AddEntryFromControl(EntryInput input)
    {
        var entry = BuildEntry(input, existing: null);
        var error = EntryValidator.ValidateForRun(entry);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        _store.Current.Entries.Add(entry);
        if (!_store.TrySave(_store.Current, out var saveError))
        {
            _store.Current.Entries.Remove(entry);
            throw new InvalidOperationException($"保存失败：{saveError}");
        }

        _supervisor.ApplyConfig(_store.Current);
        var runtime = FindRuntime(entry.Key)
            ?? throw new InvalidOperationException("保存后找不到条目");
        return ToStatus(runtime);
    }

    private EntryStatus UpdateEntryFromControl(string key, EntryInput input)
    {
        var entries = _store.Current.Entries;
        var index = FindConfigIndexByKey(key);
        if (index < 0)
        {
            throw new InvalidOperationException($"配置中找不到条目：{key}");
        }

        var existing = entries[index];
        var updated = BuildEntry(input, existing);

        var error = EntryValidator.ValidateForRun(updated);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        entries[index] = updated;
        if (!_store.TrySave(_store.Current, out var saveError))
        {
            entries[index] = existing;
            throw new InvalidOperationException($"保存失败：{saveError}");
        }

        _supervisor.ApplyConfig(_store.Current);
        var runtime = FindRuntime(updated.Key)
            ?? throw new InvalidOperationException("保存后找不到条目");
        return ToStatus(runtime);
    }

    private object RemoveEntryFromControl(string key)
    {
        var entries = _store.Current.Entries;
        var index = FindConfigIndexByKey(key);
        if (index < 0)
        {
            throw new InvalidOperationException($"配置中找不到条目：{key}");
        }

        var removed = entries[index];
        entries.RemoveAt(index);

        if (!_store.TrySave(_store.Current, out var error))
        {
            entries.Insert(index, removed);
            throw new InvalidOperationException($"保存失败：{error}");
        }

        if (_logForms.TryGetValue(removed.Key, out var logForm) && !logForm.IsDisposed)
        {
            logForm.Close();
        }

        _supervisor.ApplyConfig(_store.Current);
        return new { removed = true, id = removed.Id, name = removed.Name };
    }

    private static EntryConfig BuildEntry(EntryInput input, EntryConfig? existing)
    {
        return EntryInputMapper.Apply(input, existing);
    }

    private int FindConfigIndexByKey(string key)
    {
        var entries = _store.Current.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (string.Equals(entry.Id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Name, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private EntryRuntime? FindRuntime(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _supervisor.Snapshot().FirstOrDefault(runtime =>
            string.Equals(runtime.Config.Id, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtime.Config.Name, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtime.Config.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private object GetLogs(string? key, int lines)
    {
        var runtime = FindRuntime(key) ?? throw new InvalidOperationException($"条目不存在：{key}");
        var count = Math.Clamp(lines, 1, 5000);
        var snapshot = runtime.Log.Snapshot();

        return new
        {
            id = runtime.Config.Id,
            name = runtime.Config.Name,
            state = runtime.State.ToString().ToLowerInvariant(),
            total = runtime.Log.TotalLines,
            dropped = runtime.Log.DroppedLines,
            lines = snapshot.TakeLast(count).ToArray(),
        };
    }

    private static EntryStatus ToStatus(EntryRuntime runtime)
    {
        var config = runtime.Config;
        return new EntryStatus
        {
            Id = config.Id,
            Name = config.Name,
            Cwd = config.Cwd,
            Command = config.Command,
            Shell = config.Shell,
            Encoding = config.Encoding,
            Env = config.Env,
            AutoStart = config.AutoStart,
            State = runtime.State.ToString().ToLowerInvariant(),
            Pid = runtime.Pid,
            StartedAt = runtime.StartedAt,
            UptimeSeconds = runtime.Uptime is { } uptime ? (long)uptime.TotalSeconds : null,
            LastExitCode = runtime.LastExitCode,
            LastExitedAt = runtime.LastExitedAt,
            LastError = runtime.LastError,
            IsOrphan = runtime.IsOrphan,
            LogTotal = runtime.Log.TotalLines,
            LogDropped = runtime.Log.DroppedLines,
        };
    }

    private T UiInvoke<T>(Func<T> func)
    {
        if (IsDisposed || Disposing)
        {
            throw new InvalidOperationException("司命正在退出");
        }

        return InvokeRequired ? (T)Invoke(func) : func();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyListColumnWidths(DeviceDpi);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyListColumnWidths(e.DeviceDpiNew);
    }

    private void ApplyListColumnWidths(int dpi)
    {
        if (dpi <= 0 || _list.Columns.Count < FixedListColumnLogicalWidths.Length + 1)
        {
            return;
        }

        var scale = dpi / 96.0;
        var used = 0;

        for (var index = 0; index < FixedListColumnLogicalWidths.Length; index++)
        {
            var width = Math.Max(1, (int)Math.Round(FixedListColumnLogicalWidths[index] * scale));
            _list.Columns[index].Width = width;
            used += width;
        }

        // 最后一列吃掉剩余宽度，并预留垂直滚动条的位置，避免默认出现水平滚动条。
        var reserve = Math.Max(1, (int)Math.Round(ColumnScrollbarReserveLogical * scale));
        _list.Columns[^1].Width = Math.Max(
            (int)Math.Round(140 * scale),
            _list.ClientSize.Width - used - reserve);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!_hideHintShown)
                {
                    _hideHintShown = true;
                    Balloon("司命仍在托盘运行", "双击托盘图标或右键菜单可重新打开。");
                }

                return;
            }

            _exiting = true;
            _supervisor.Shutdown(keepRunning: false);
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uptimeTimer.Stop();
        _uptimeTimer.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _controlServer?.Dispose();
        _controlServer = null;
        _idleIcon?.Dispose();
        _runningIcon?.Dispose();
        _statusImages.Dispose();
        _supervisor.Dispose();
        _store.Dispose();

        base.OnFormClosed(e);
    }

    public static string FormatDuration(TimeSpan? value)
    {
        if (value is null)
        {
            return "—";
        }

        var time = value.Value;
        if (time.TotalDays >= 1)
        {
            return $"{(int)time.TotalDays}d {time.Hours}h";
        }

        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}h {time.Minutes:00}m";
        }

        if (time.TotalMinutes >= 1)
        {
            return $"{(int)time.TotalMinutes}m {time.Seconds:00}s";
        }

        return $"{Math.Max(0, time.Seconds)}s";
    }

    private EntryRuntime? Selected =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as EntryRuntime : null;

    private static ToolStripButton ToolbarButton(string text, Action action)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static ToolStripMenuItem MenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshList()
    {
        var selectedKey = Selected?.Config.Key;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _items.Clear();

            foreach (var entry in _supervisor.Snapshot())
            {
                var item = new ListViewItem(entry.Config.Name)
                {
                    Tag = entry,
                    ImageKey = ImageKeyFor(entry),
                    ToolTipText = BuildTooltip(entry),
                };
                item.SubItems.Add(StatusText(entry));
                item.SubItems.Add(FormatDuration(entry.Uptime));
                item.SubItems.Add(entry.Config.AutoStart ? "是" : "");
                item.SubItems.Add(entry.Config.Command);
                item.SubItems.Add(entry.Config.Cwd);
                item.UseItemStyleForSubItems = false;
                item.SubItems[1].ForeColor = StateColor(entry);
                _list.Items.Add(item);
                _items[entry] = item;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        if (selectedKey is not null)
        {
            SelectByKey(selectedKey);
        }

        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
            _list.Items[0].Focused = true;
        }

        _emptyLabel.Visible = _list.Items.Count == 0;

        UpdateButtons();
        UpdateCounts();
        UpdateTrayIcon();
    }

    private void SelectByKey(string key)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is EntryRuntime entry
                && string.Equals(entry.Config.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
                item.Focused = true;
                return;
            }
        }
    }

    private void OnEntryStateChanged(EntryRuntime entry)
    {
        if (!_items.TryGetValue(entry, out var item))
        {
            RefreshList();
            return;
        }

        item.ImageKey = ImageKeyFor(entry);
        item.SubItems[1].Text = StatusText(entry);
        item.SubItems[1].ForeColor = StateColor(entry);
        item.SubItems[2].Text = FormatDuration(entry.Uptime);
        item.ToolTipText = BuildTooltip(entry);

        UpdateButtons();
        UpdateCounts();
        UpdateTrayIcon();
    }

    private void UpdateRuntimeCells()
    {
        foreach (var (entry, item) in _items)
        {
            if (item.SubItems.Count >= 3)
            {
                item.SubItems[2].Text = FormatDuration(entry.Uptime);
            }
        }

        UpdateCounts();
    }

    private void UpdateButtons()
    {
        var entry = Selected;
        var index = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        var editable = entry is not null && !entry.IsOrphan;

        _startButton.Enabled = entry?.State == EntryState.Stopped;
        _stopButton.Enabled = entry?.State is EntryState.Running or EntryState.Starting;
        _restartButton.Enabled = entry?.State == EntryState.Running;
        _logButton.Enabled = entry is not null;
        _addButton.Enabled = true;
        _editButton.Enabled = editable;
        _deleteButton.Enabled = editable;
        _upButton.Enabled = editable && index > 0;
        _downButton.Enabled = editable && index >= 0 && index < _list.Items.Count - 1;
    }

    private void UpdateCounts()
    {
        var all = _supervisor.Snapshot();
        var running = all.Count(entry => entry.IsRunning);
        _countLabel.Text = $"运行中 {running}/{all.Length}";
        _tray.Text = $"司命 · 运行中 {running}/{all.Length}";
    }

    private void UpdateTrayIcon()
    {
        var running = _supervisor.Snapshot().Any(entry => entry.IsRunning);
        var generated = running
            ? (_runningIcon ??= TrayIconFactory.Create(running: true))
            : (_idleIcon ??= TrayIconFactory.Create(running: false));

        if (!ReferenceEquals(_tray.Icon, generated.Icon))
        {
            _tray.Icon = generated.Icon;
        }
    }

    private void OnConfigChanged(ConfigLoadResult result)
    {
        if (result.Success && result.Config is not null)
        {
            _supervisor.ApplyConfig(result.Config);
            ReconcileStartupSetting();
            if (!ReportExtractError())
            {
                SetStatus($"配置已重载 {DateTime.Now:HH:mm:ss}");
            }
        }
        else
        {
            SetStatus($"配置有误：{result.Error}", error: true);
        }
    }

    /// <summary>提取规则写错时只在状态栏提示，不阻塞启动与配置加载。</summary>
    private bool ReportExtractError()
    {
        var error = _supervisor.Snapshot()
            .Select(entry => entry.Extractor.ConfigurationError)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        if (error is null)
        {
            return false;
        }

        SetStatus($"提取规则有误：{error}", error: true);
        return true;
    }

    private void ReconcileStartupSetting()
    {
        var enabled = _store.Current.Settings.StartWithWindows;
        var executable = ExecutablePath;

        if (enabled)
        {
            if (!StartupRegistration.IsEnabled(executable)
                && !StartupRegistration.SetEnabled(true, executable, out var error))
            {
                SetStatus($"写入开机自启失败：{error}", error: true);
            }
        }
        else
        {
            // 配置是唯一事实来源：关闭时顺手清掉可能残留的启动项。
            StartupRegistration.SetEnabled(false, executable, out _);
        }

        SyncStartupButton();
    }

    private void ApplyStartup(bool enabled)
    {
        var settings = _store.Current.Settings;
        var previous = settings.StartWithWindows;
        if (previous == enabled)
        {
            SyncStartupButton();
            return;
        }

        settings.StartWithWindows = enabled;
        if (!_store.TrySave(_store.Current, out var saveError))
        {
            settings.StartWithWindows = previous;
            SetStatus($"保存配置失败：{saveError}", error: true);
            SyncStartupButton();
            return;
        }

        if (!StartupRegistration.SetEnabled(enabled, ExecutablePath, out var registryError))
        {
            settings.StartWithWindows = previous;
            _store.TrySave(_store.Current, out _);
            SetStatus($"开机自启设置失败：{registryError}", error: true);
            SyncStartupButton();
            return;
        }

        SetStatus(enabled ? "已开启开机自启" : "已关闭开机自启");
        SyncStartupButton();
    }

    private void SyncStartupButton()
    {
        _syncingStartup = true;
        try
        {
            _startupButton.Checked = _store.Current.Settings.StartWithWindows;
        }
        finally
        {
            _syncingStartup = false;
        }
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    private async Task StartSelectedAsync()
    {
        if (Selected is { } entry)
        {
            await StartEntryAsync(entry);
        }
    }

    private async Task StopSelectedAsync()
    {
        if (Selected is { } entry)
        {
            await StopEntryAsync(entry);
        }
    }

    private async Task RestartSelectedAsync()
    {
        if (Selected is { } entry)
        {
            await RestartEntryAsync(entry);
        }
    }

    private void AddEntry()
    {
        using var dialog = new EntryEditForm(
            source: null,
            otherNames: _store.Current.Entries.Select(entry => entry.Name));

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var entry = dialog.Result;
        _store.Current.Entries.Add(entry);

        if (!_store.TrySave(_store.Current, out var error))
        {
            _store.Current.Entries.Remove(entry);
            SetStatus($"保存失败：{error}", error: true);
            return;
        }

        _supervisor.ApplyConfig(_store.Current);
        SelectByKey(entry.Key);
        SetStatus($"已新增「{entry.Name}」");
    }

    private async Task EditSelectedAsync()
    {
        var runtime = Selected;
        if (runtime is null || runtime.IsOrphan)
        {
            return;
        }

        var entries = _store.Current.Entries;
        var index = FindConfigIndex(runtime);
        if (index < 0)
        {
            return;
        }

        var old = entries[index];
        var otherNames = entries
            .Where((_, entryIndex) => entryIndex != index)
            .Select(entry => entry.Name);

        using var dialog = new EntryEditForm(old, otherNames);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var updated = dialog.Result;
        var needsRestart = runtime.State != EntryState.Stopped && NeedsRestart(old, updated);

        entries[index] = updated;
        if (!_store.TrySave(_store.Current, out var error))
        {
            entries[index] = old;
            SetStatus($"保存失败：{error}", error: true);
            return;
        }

        _supervisor.ApplyConfig(_store.Current);
        SelectByKey(updated.Key);

        if (needsRestart)
        {
            var choice = MessageBox.Show(
                this,
                "条目正在运行，新配置需要重启后生效。现在重启？",
                "司命",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Yes)
            {
                await _supervisor.RestartAsync(runtime);
            }
        }

        SetStatus($"已保存「{updated.Name}」");
    }

    private async Task DeleteSelectedAsync()
    {
        var runtime = Selected;
        if (runtime is null || runtime.IsOrphan)
        {
            return;
        }

        var entries = _store.Current.Entries;
        var index = FindConfigIndex(runtime);
        if (index < 0)
        {
            return;
        }

        var running = runtime.State != EntryState.Stopped;
        var message = running
            ? $"「{runtime.Config.Name}」正在运行。停止并删除？"
            : $"删除条目「{runtime.Config.Name}」？";

        if (MessageBox.Show(
                this,
                message,
                "删除条目",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        if (running)
        {
            await _supervisor.StopAsync(runtime);
            if (runtime.State != EntryState.Stopped)
            {
                SetStatus("停止失败，未删除", error: true);
                return;
            }
        }

        var removed = entries[index];
        entries.RemoveAt(index);

        if (!_store.TrySave(_store.Current, out var error))
        {
            entries.Insert(index, removed);
            SetStatus($"保存失败：{error}", error: true);
            return;
        }

        if (_logForms.TryGetValue(removed.Key, out var logForm) && !logForm.IsDisposed)
        {
            logForm.Close();
        }

        _supervisor.ApplyConfig(_store.Current);
        SetStatus($"已删除「{removed.Name}」");
    }

    private void MoveSelected(int delta)
    {
        var runtime = Selected;
        if (runtime is null || runtime.IsOrphan)
        {
            return;
        }

        var entries = _store.Current.Entries;
        var index = FindConfigIndex(runtime);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= entries.Count)
        {
            return;
        }

        (entries[index], entries[target]) = (entries[target], entries[index]);

        if (!_store.TrySave(_store.Current, out var error))
        {
            (entries[index], entries[target]) = (entries[target], entries[index]);
            SetStatus($"保存失败：{error}", error: true);
            return;
        }

        _supervisor.ApplyConfig(_store.Current);
        SelectByKey(runtime.Config.Key);
        SetStatus("顺序已保存");
    }

    private int FindConfigIndex(EntryRuntime runtime)
    {
        var entries = _store.Current.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            if (string.Equals(entries[index].Key, runtime.Config.Key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool NeedsRestart(EntryConfig before, EntryConfig after)
    {
        return !string.Equals(before.Command, after.Command, StringComparison.Ordinal)
            || !string.Equals(before.Cwd, after.Cwd, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.Shell, after.Shell, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.Encoding, after.Encoding, StringComparison.OrdinalIgnoreCase)
            || !EnvironmentEquals(before.Env, after.Env);
    }

    private static bool EnvironmentEquals(
        IReadOnlyDictionary<string, string?>? left,
        IReadOnlyDictionary<string, string?>? right)
    {
        var leftCount = left?.Count ?? 0;
        var rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
        {
            return false;
        }

        if (left is null || right is null)
        {
            return true;
        }

        foreach (var pair in left)
        {
            var found = false;
            foreach (var other in right)
            {
                if (string.Equals(pair.Key, other.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(pair.Value, other.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private async Task StartEntryAsync(EntryRuntime entry)
    {        await _supervisor.StartAsync(entry);
        if (entry.LastError is { } error)
        {
            SetStatus($"「{entry.Config.Name}」启动失败：{error}", error: true);
            Balloon("启动失败", $"「{entry.Config.Name}」：{error}");
        }
        else
        {
            SetStatus($"已启动「{entry.Config.Name}」");
        }
    }

    private async Task StopEntryAsync(EntryRuntime entry)
    {
        await _supervisor.StopAsync(entry);
        SetStatus($"已停止「{entry.Config.Name}」");
    }

    private async Task RestartEntryAsync(EntryRuntime entry)
    {
        await _supervisor.RestartAsync(entry);
        if (entry.LastError is { } error)
        {
            SetStatus($"「{entry.Config.Name}」重启失败：{error}", error: true);
            Balloon("重启失败", $"「{entry.Config.Name}」：{error}");
        }
        else
        {
            SetStatus($"已重启「{entry.Config.Name}」");
        }
    }

    private async Task StartAllAsync()
    {
        await _supervisor.StartAllAsync();
        SetStatus("全部启动完成");
    }

    private async Task StopAllAsync()
    {
        await _supervisor.StopAllAsync();
        SetStatus("全部停止完成");
    }

    private void OpenLog(EntryRuntime entry)
    {
        var key = entry.Config.Key;
        if (_logForms.TryGetValue(key, out var existing) && !existing.IsDisposed)
        {
            existing.Activate();
            return;
        }

        var form = new LogForm(entry, _supervisor);
        form.FormClosed += (_, _) => _logForms.Remove(key);
        _logForms[key] = form;
        form.Show();
    }

    private void OpenDirectory(EntryRuntime entry)
    {
        if (!Directory.Exists(entry.Config.Cwd))
        {
            SetStatus($"工作目录不存在：{entry.Config.Cwd}", error: true);
            Balloon("打开失败", $"工作目录不存在：{entry.Config.Cwd}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.Config.Cwd}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetStatus($"打开工作目录失败：{ex.Message}", error: true);
        }
    }

    private void CopyCommand(EntryRuntime entry)
    {
        try
        {
            Clipboard.SetText(entry.Config.Command);
            SetStatus($"已复制「{entry.Config.Name}」的命令");
        }
        catch (Exception ex)
        {
            SetStatus($"复制失败：{ex.Message}", error: true);
        }
    }

    /// <summary>条目级「随司命启动」：勾选即写回配置，失败回滚。</summary>
    private ToolStripMenuItem BuildAutoStartItem(EntryRuntime entry)
    {
        var item = new ToolStripMenuItem("随司命启动")
        {
            Checked = entry.Config.AutoStart,
            // 自己管勾选状态：保存失败要能回滚。
            CheckOnClick = false,
        };

        item.Click += (_, _) => SetAutoStart(entry, item, !entry.Config.AutoStart);
        return item;
    }

    private void SetAutoStart(EntryRuntime entry, ToolStripMenuItem item, bool enabled)
    {
        var config = entry.Config;
        var previous = config.AutoStart;
        if (previous == enabled)
        {
            item.Checked = enabled;
            return;
        }

        config.AutoStart = enabled;
        if (_store.TrySave(_store.Current, out var error))
        {
            item.Checked = enabled;
            RefreshList();
            SetStatus(enabled
                ? $"「{config.Name}」将在司命启动时自动拉起"
                : $"「{config.Name}」不再自动拉起");
            return;
        }

        config.AutoStart = previous;
        item.Checked = previous;
        SetStatus($"保存配置失败：{error}", error: true);
    }

    /// <summary>执行条目子菜单里的快捷动作；失败只出气泡，不改条目状态。</summary>
    private void RunAction(EntryRuntime entry, ActionKind kind, ActionConfig action, string payload)
    {
        try
        {
            switch (kind)
            {
                case ActionKind.Copy:
                    Clipboard.SetText(payload);
                    SetStatus($"已复制「{entry.Config.Name}」的{action.Label.Trim()}");
                    break;
                case ActionKind.Open:
                    Process.Start(new ProcessStartInfo(payload) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"动作「{action.Label}」失败：{ex.Message}", error: true);
            Balloon("动作失败", $"「{action.Label}」：{ex.Message}");
        }
    }

    private void OpenConfigFile()
    {
        try
        {
            if (File.Exists(_store.FilePath))
            {
                Process.Start(new ProcessStartInfo(_store.FilePath) { UseShellExecute = true });
            }
            else
            {
                OpenConfigFolder(selectFile: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"打开配置失败：{ex.Message}", error: true);
        }
    }

    private void OpenConfigFolder(bool selectFile)
    {
        try
        {
            var directory = Path.GetDirectoryName(_store.FilePath) ?? AppContext.BaseDirectory;
            var arguments = selectFile && File.Exists(_store.FilePath)
                ? $"/select,\"{_store.FilePath}\""
                : $"\"{directory}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"打开目录失败：{ex.Message}", error: true);
        }
    }

    private void BuildTrayMenu()
    {
        // 先复制再清理：Dispose 会把自己从 Items 里移除，
        // 直接遍历会触发 “Collection was modified”。
        var previousItems = _trayMenu.Items.Cast<ToolStripItem>().ToArray();
        _trayMenu.Items.Clear();
        foreach (var item in previousItems)
        {
            item.Dispose();
        }

        var entries = _supervisor.Snapshot();
        foreach (var entry in entries)
        {
            var parent = new ToolStripMenuItem(FormatTrayLabel(entry)) { Tag = entry };

            if (entry.State == EntryState.Running)
            {
                parent.DropDownItems.Add(MenuItem("重启", () => _ = RestartEntryAsync(entry)));
                parent.DropDownItems.Add(MenuItem("停止", () => _ = StopEntryAsync(entry)));
            }
            else if (entry.State == EntryState.Starting)
            {
                parent.DropDownItems.Add(MenuItem("取消启动", () => _ = StopEntryAsync(entry)));
            }
            else if (entry.State == EntryState.Stopped)
            {
                parent.DropDownItems.Add(MenuItem("启动", () => _ = StartEntryAsync(entry)));
            }

            if (parent.DropDownItems.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
            }

            parent.DropDownItems.Add(MenuItem("查看日志", () => OpenLog(entry)));
            parent.DropDownItems.Add(MenuItem("打开工作目录", () => OpenDirectory(entry)));
            parent.DropDownItems.Add(MenuItem("复制命令", () => CopyCommand(entry)));
            parent.DropDownItems.Add(BuildAutoStartItem(entry));

            var actionItems = TrayActionMenu.Build(entry, RunAction);
            if (actionItems.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                foreach (var actionItem in actionItems)
                {
                    parent.DropDownItems.Add(actionItem);
                }
            }

            _trayMenu.Items.Add(parent);
        }

        if (entries.Length > 0)
        {
            _trayMenu.Items.Add(new ToolStripSeparator());
        }

        var startAll = MenuItem("全部启动", () => _ = StartAllAsync());
        startAll.Enabled = entries.Any(entry => entry.State == EntryState.Stopped);
        _trayMenu.Items.Add(startAll);

        var stopAll = MenuItem("全部停止", () => _ = StopAllAsync());
        stopAll.Enabled = entries.Any(entry => entry.State != EntryState.Stopped);
        _trayMenu.Items.Add(stopAll);

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(MenuItem("打开主窗口", ShowFromTray));
        _trayMenu.Items.Add(MenuItem("打开配置文件所在文件夹", () => OpenConfigFolder(selectFile: true)));

        var startupItem = new ToolStripMenuItem("开机自启")
        {
            Checked = _store.Current.Settings.StartWithWindows,
            CheckOnClick = true,
        };
        startupItem.CheckedChanged += (_, _) => ApplyStartup(startupItem.Checked);
        _trayMenu.Items.Add(startupItem);

        _trayMenu.Items.Add(MenuItem("退出", RequestExit));
    }

    private void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        var running = _supervisor.Snapshot().Where(entry => entry.IsRunning).ToArray();
        var keepRunning = false;

        if (running.Length > 0)
        {
            using var dialog = new ExitConfirmForm(running);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            keepRunning = dialog.KeepRunning;
        }

        _exiting = true;
        _uptimeTimer.Stop();
        _tray.Visible = false;

        foreach (var form in _logForms.Values.ToArray())
        {
            form.Close();
        }

        _supervisor.Shutdown(keepRunning);
        Application.Exit();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            e.Handled = true;
            if (Selected is { } entry)
            {
                if (entry.State == EntryState.Stopped)
                {
                    _ = StartEntryAsync(entry);
                }
                else
                {
                    _ = StopEntryAsync(entry);
                }
            }
        }
        else if (e.Control && e.KeyCode == Keys.R)
        {
            e.Handled = true;
            _ = RestartSelectedAsync();
        }
        else if (e.Control && e.KeyCode == Keys.L)
        {
            e.Handled = true;
            if (Selected is { } entry)
            {
                OpenLog(entry);
            }
        }
        else if (e.Control && e.KeyCode == Keys.N)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            AddEntry();
        }
        else if (e.KeyCode is Keys.Enter or Keys.F2)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = EditSelectedAsync();
        }
        else if (e.KeyCode == Keys.Delete)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = DeleteSelectedAsync();
        }
        else if (e.Control && e.KeyCode == Keys.Up)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            MoveSelected(-1);
        }
        else if (e.Control && e.KeyCode == Keys.Down)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            MoveSelected(1);
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        _messageLabel.Text = text;
        _messageLabel.ForeColor = error ? ErrorColor : SystemColors.ControlText;
    }

    private void Balloon(string title, string text)
    {
        try
        {
            _tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Warning);
        }
        catch (Exception)
        {
        }
    }

    private static string ImageKeyFor(EntryRuntime entry) => entry.State switch
    {
        EntryState.Running => "running",
        EntryState.Starting => "starting",
        EntryState.Stopping => "stopping",
        _ => "stopped",
    };

    private static Color StateColor(EntryRuntime entry) => entry.State switch
    {
        EntryState.Running => RunningColor,
        EntryState.Starting => StartingColor,
        EntryState.Stopping => StoppingColor,
        _ => entry.LastError is not null ? ErrorColor : StoppedColor,
    };

    private static string StatusText(EntryRuntime entry)
    {
        var text = entry.State switch
        {
            EntryState.Running => "运行中",
            EntryState.Starting => "启动中",
            EntryState.Stopping => "停止中",
            _ => "已停止",
        };

        if (entry.State == EntryState.Stopped && entry.LastExitCode is int code && code != 0)
        {
            text += $" ({code})";
        }

        if (entry.State == EntryState.Stopped && entry.LastError is not null)
        {
            text += " · 失败";
        }

        if (entry.IsOrphan)
        {
            text += " · 配置已移除";
        }

        return text;
    }

    private static string BuildTooltip(EntryRuntime entry)
    {
        var text = $"{entry.Config.Command}\n{entry.Config.Cwd}";
        if (entry.LastError is { } error)
        {
            text += $"\n错误：{error}";
        }

        return text;
    }

    private static string FormatTrayLabel(EntryRuntime entry)
    {
        var mark = entry.State switch
        {
            EntryState.Running => "●",
            EntryState.Starting or EntryState.Stopping => "◐",
            _ => "○",
        };

        var name = entry.Config.Name.Length > 24
            ? entry.Config.Name[..24] + "…"
            : entry.Config.Name;

        var state = entry.State switch
        {
            EntryState.Running => FormatDuration(entry.Uptime),
            EntryState.Starting => "启动中",
            EntryState.Stopping => "停止中",
            _ => "已停止",
        };

        return $"{mark} {name}    {state}";
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
}
