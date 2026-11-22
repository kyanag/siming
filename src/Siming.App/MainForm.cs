using System.Diagnostics;
using Siming.Core.Configuration;
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

    private GeneratedIcon? _idleIcon;
    private GeneratedIcon? _runningIcon;
    private bool _exiting;
    private bool _hideHintShown;
    private bool _syncingStartup;

    public MainForm(ConfigStore store, ProcessSupervisor supervisor)
    {
        _store = store;
        _supervisor = supervisor;

        Text = "司命";
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
        _list.Columns.Add("状态", 130);
        _list.Columns.Add("运行时长", 100);
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

        _trayMenu = new ContextMenuStrip { ShowImageMargin = false };
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
        _startButton.Enabled = entry?.State == EntryState.Stopped;
        _stopButton.Enabled = entry?.State is EntryState.Running or EntryState.Starting;
        _restartButton.Enabled = entry?.State == EntryState.Running;
        _logButton.Enabled = entry is not null;
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
            SetStatus($"配置已重载 {DateTime.Now:HH:mm:ss}");
        }
        else
        {
            SetStatus($"配置有误：{result.Error}", error: true);
        }
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

    private async Task StartEntryAsync(EntryRuntime entry)
    {
        await _supervisor.StartAsync(entry);
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
        foreach (ToolStripItem item in _trayMenu.Items)
        {
            item.Dispose();
        }

        _trayMenu.Items.Clear();

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
