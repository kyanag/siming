using Siming.Core.Processes;

namespace Siming.App;

public sealed class ExitConfirmForm : Form
{
    public ExitConfirmForm(IReadOnlyList<EntryRuntime> running)
    {
        Icon = AppIcon.Shared;
        Text = "退出司命";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 270);

        var names = string.Join(
            Environment.NewLine,
            running.Take(6).Select(entry => $"  ● {entry.Config.Name}"));

        if (running.Count > 6)
        {
            names += $"{Environment.NewLine}  … 等 {running.Count} 个";
        }

        var message = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 8),
            Text = $"还有 {running.Count} 个进程在运行：{Environment.NewLine}{Environment.NewLine}{names}"
                + $"{Environment.NewLine}{Environment.NewLine}"
                + "保留进程意味着它们继续运行，但司命不再管理它们。",
        };

        var endAll = new Button
        {
            Text = "结束所有并退出",
            DialogResult = DialogResult.OK,
            Size = new Size(140, 32),
            Margin = new Padding(6),
        };

        var keep = new Button
        {
            Text = "保留进程并退出",
            Size = new Size(140, 32),
            Margin = new Padding(6),
        };
        keep.Click += (_, _) =>
        {
            KeepRunning = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 32),
            Margin = new Padding(6),
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 6, 12, 6),
            WrapContents = false,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(keep);
        buttons.Controls.Add(endAll);

        Controls.Add(message);
        Controls.Add(buttons);

        AcceptButton = endAll;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public bool KeepRunning { get; private set; }
}
