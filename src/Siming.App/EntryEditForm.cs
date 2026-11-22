using Siming.Core.Configuration;

namespace Siming.App;

public sealed class EntryEditForm : Form
{
    private readonly EntryConfig _source;
    private readonly HashSet<string> _otherNames;
    private readonly TextBox _nameBox;
    private readonly TextBox _cwdBox;
    private readonly TextBox _commandBox;
    private readonly ComboBox _shellBox;
    private readonly ComboBox _encodingBox;
    private readonly DataGridView _envGrid;
    private readonly Label _errorLabel;
    private readonly Button _saveButton;

    public EntryEditForm(EntryConfig? source, IEnumerable<string> otherNames)
    {
        _source = source ?? new EntryConfig();
        _otherNames = new HashSet<string>(
            otherNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        Icon = AppIcon.Shared;
        Text = source is null ? "新增条目" : "编辑条目";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(580, 528);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 10),
            ColumnCount = 2,
            RowCount = 8,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var index = 0; index < 5; index++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _nameBox = new TextBox { Dock = DockStyle.Fill, Text = _source.Name };
        _cwdBox = new TextBox { Dock = DockStyle.Fill, Text = _source.Cwd };
        _commandBox = new TextBox { Dock = DockStyle.Fill, Text = _source.Command };

        var browseButton = new Button
        {
            Text = "…",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(4, 0, 0, 0),
        };
        browseButton.Click += (_, _) => BrowseCwd();

        var cwdPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        cwdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cwdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        cwdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cwdPanel.Controls.Add(_cwdBox, 0, 0);
        cwdPanel.Controls.Add(browseButton, 1, 0);

        _shellBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        // 顺序即优先级：pwsh → powershell → cmd → 直接执行。
        _shellBox.Items.AddRange(["pwsh", "powershell", "cmd", "none"]);
        var initialShell = source is null ? ShellKinds.Preferred() : _source.Shell;
        _shellBox.SelectedItem = ShellKinds.Parse(initialShell) switch
        {
            ShellKind.Pwsh => "pwsh",
            ShellKind.PowerShell => "powershell",
            ShellKind.None => "none",
            _ => "cmd",
        };

        _encodingBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _encodingBox.Items.AddRange(["utf-8", "gbk"]);
        _encodingBox.SelectedItem = _source.Encoding is "gbk" or "gb2312" or "gb18030" or "936"
            ? "gbk"
            : "utf-8";

        _envGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 4),
        };
        _envGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "键",
            FillWeight = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _envGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "值",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        if (_source.Env is not null)
        {
            foreach (var pair in _source.Env)
            {
                _envGrid.Rows.Add(pair.Key, pair.Value ?? "");
            }
        }

        _errorLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(0xC0, 0x39, 0x2B),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0),
        };

        _saveButton = new Button
        {
            Text = "保存",
            Size = new Size(96, 30),
            Margin = new Padding(6),
        };
        _saveButton.Click += (_, _) => SaveEntry();

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(96, 30),
            Margin = new Padding(6),
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_saveButton);

        table.Controls.Add(FieldLabel("名称"), 0, 0);
        table.Controls.Add(_nameBox, 1, 0);
        table.Controls.Add(FieldLabel("工作目录"), 0, 1);
        table.Controls.Add(cwdPanel, 1, 1);
        table.Controls.Add(FieldLabel("命令"), 0, 2);
        table.Controls.Add(_commandBox, 1, 2);
        table.Controls.Add(FieldLabel("Shell"), 0, 3);
        table.Controls.Add(_shellBox, 1, 3);
        table.Controls.Add(FieldLabel("编码"), 0, 4);
        table.Controls.Add(_encodingBox, 1, 4);
        table.Controls.Add(FieldLabel("环境变量"), 0, 5);
        table.Controls.Add(_envGrid, 1, 5);
        table.Controls.Add(_errorLabel, 0, 6);
        table.SetColumnSpan(_errorLabel, 2);
        table.Controls.Add(buttons, 0, 7);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);

        AcceptButton = _saveButton;
        CancelButton = cancelButton;

        _nameBox.TextChanged += (_, _) => UpdateValidation();
        _cwdBox.TextChanged += (_, _) => UpdateValidation();
        _commandBox.TextChanged += (_, _) => UpdateValidation();
        _shellBox.SelectedIndexChanged += (_, _) => UpdateValidation();
        _encodingBox.SelectedIndexChanged += (_, _) => UpdateValidation();
        _envGrid.CellValueChanged += (_, _) => UpdateValidation();
        _envGrid.RowsRemoved += (_, _) => UpdateValidation();
        _envGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_envGrid.IsCurrentCellDirty)
            {
                _envGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        UpdateValidation();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public EntryConfig? Result { get; private set; }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 8, 6, 0),
    };

    private void BrowseCwd()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择工作目录",
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_cwdBox.Text.Trim()) ? _cwdBox.Text.Trim() : "",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cwdBox.Text = dialog.SelectedPath;
        }
    }

    private void SaveEntry()
    {
        var error = ValidateInputs();
        if (error is not null)
        {
            _errorLabel.Text = error;
            return;
        }

        Result = new EntryConfig
        {
            Id = string.IsNullOrWhiteSpace(_source.Id) ? Guid.NewGuid().ToString("N") : _source.Id,
            Name = _nameBox.Text.Trim(),
            Cwd = _cwdBox.Text.Trim(),
            Command = _commandBox.Text.Trim(),
            Shell = (string)(_shellBox.SelectedItem ?? "cmd"),
            Encoding = (string)(_encodingBox.SelectedItem ?? "utf-8"),
            Env = BuildEnvironment(),
            // 随司命启动现在只在托盘菜单里切换，编辑对话框原样保留。
            AutoStart = _source.AutoStart,
            Actions = _source.Actions,
            Extract = _source.Extract,
            Extra = _source.Extra,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateValidation()
    {
        var error = ValidateInputs();
        _errorLabel.Text = error ?? "";
        _saveButton.Enabled = error is null;
    }

    private string? ValidateInputs()
    {
        var name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            return "名称不能为空";
        }

        if (_otherNames.Contains(name))
        {
            return "名称已存在";
        }

        var cwd = _cwdBox.Text.Trim();
        if (cwd.Length == 0)
        {
            return "工作目录不能为空";
        }

        if (!Directory.Exists(cwd))
        {
            return "工作目录不存在";
        }

        if (_commandBox.Text.Trim().Length == 0)
        {
            return "命令不能为空";
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _envGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var key = (row.Cells[0].Value as string)?.Trim() ?? "";
            var value = row.Cells[1].Value as string ?? "";
            if (key.Length == 0)
            {
                if (value.Length > 0)
                {
                    return "环境变量名不能为空";
                }

                continue;
            }

            if (key.Contains('='))
            {
                return $"环境变量名非法：{key}";
            }

            if (!keys.Add(key))
            {
                return $"环境变量重复：{key}";
            }
        }

        return null;
    }

    private Dictionary<string, string?>? BuildEnvironment()
    {
        Dictionary<string, string?>? environment = null;
        foreach (DataGridViewRow row in _envGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var key = (row.Cells[0].Value as string)?.Trim() ?? "";
            if (key.Length == 0)
            {
                continue;
            }

            environment ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var value = row.Cells[1].Value as string ?? "";
            // 空值表示删除该变量（CreateProcess 环境块会跳过 null）。
            environment[key] = value.Length == 0 ? null : value;
        }

        return environment;
    }
}
