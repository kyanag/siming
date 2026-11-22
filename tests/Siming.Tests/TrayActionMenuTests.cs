using Siming.App;
using Siming.Core.Configuration;
using Siming.Core.Processes;

namespace Siming.Tests;

public sealed class TrayActionMenuTests
{
    [Fact]
    public void Build_ReturnsNothing_WhenNoActionsConfigured()
    {
        using var supervisor = new ProcessSupervisor(new AppConfig
        {
            Entries = [Entry(actions: null)],
        });

        var items = TrayActionMenu.Build(supervisor.Snapshot()[0], (_, _, _, _) => { });

        Assert.Empty(items);
    }

    [Fact]
    public void Build_ReturnsNothing_WhenAllLabelsAreBlank()
    {
        using var supervisor = new ProcessSupervisor(new AppConfig
        {
            Entries =
            [
                Entry(
                [
                    new ActionConfig { Label = "  ", Type = "copy", Text = "x" },
                ]),
            ],
        });

        var items = TrayActionMenu.Build(supervisor.Snapshot()[0], (_, _, _, _) => { });

        Assert.Empty(items);
    }

    [Fact]
    public void Build_KeepsItemsButDisablesThem_WhenValuesAreMissing()
    {
        using var supervisor = new ProcessSupervisor(new AppConfig
        {
            Entries =
            [
                Entry(
                [
                    new ActionConfig { Label = "打开链接", Type = "open", Target = "${url}" },
                    new ActionConfig { Label = "复制链接", Type = "copy", Text = "${url}" },
                    new ActionConfig { Label = "复制密码", Type = "copy", Text = "${password}" },
                    new ActionConfig { Label = "跑测试", Type = "command", Command = "npm test" },
                    new ActionConfig { Label = "怪动作", Type = "teleport", Target = "x" },
                ]),
            ],
        });

        var items = TrayActionMenu.Build(supervisor.Snapshot()[0], (_, _, _, _) => { });

        Assert.Equal(
            ["打开链接", "复制链接", "复制密码", "跑测试", "怪动作"],
            items.Select(item => item.Text));

        // 没提取到值：置灰但不隐藏。
        Assert.All(items, item => Assert.False(item.Enabled));
        Assert.Equal("尚未支持（v2-b）", items[3].ToolTipText);
        Assert.StartsWith("未知的动作类型", items[4].ToolTipText);
        Assert.Equal("等待程序输出所需的值", items[0].ToolTipText);
    }

    [Fact]
    public async Task Build_EnablesActions_WhenValuesAreExtracted()
    {
        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Name = "echo",
                    Cwd = Path.GetTempPath(),
                    Command = "echo server listening on http://127.0.0.1:4096 & ping -t 127.0.0.1",
                    Shell = "cmd",
                    Extract = new Dictionary<string, ExtractRule>
                    {
                        ["url"] = new() { Regex = @"listening on (http://\S+)" },
                    },
                    Actions =
                    [
                        new ActionConfig { Label = "打开链接", Type = "open", Target = "${url}" },
                        new ActionConfig { Label = "复制链接", Type = "copy", Text = "${url}/docs" },
                    ],
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(
            config,
            gracefulStopTimeout: TimeSpan.FromMilliseconds(400),
            forceStopTimeout: TimeSpan.FromSeconds(2));

        var entry = supervisor.Snapshot()[0];
        await supervisor.StartAsync(entry);

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !entry.Extractor.TryGetValue("url", out _))
        {
            await Task.Delay(50);
        }

        var calls = new List<(ActionKind Kind, string Payload)>();
        var items = TrayActionMenu.Build(entry, (_, kind, _, payload) => calls.Add((kind, payload)));

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.Enabled));

        items[0].PerformClick();
        items[1].PerformClick();

        Assert.Equal(
            [
                (ActionKind.Open, "http://127.0.0.1:4096"),
                (ActionKind.Copy, "http://127.0.0.1:4096/docs"),
            ],
            calls);

        await supervisor.StopAsync(entry);

        // 停止后值被清空，重建的菜单重新置灰。
        var stopped = TrayActionMenu.Build(entry, (_, _, _, _) => { });
        Assert.Equal(2, stopped.Count);
        Assert.All(stopped, item => Assert.False(item.Enabled));
    }

    private static EntryConfig Entry(List<ActionConfig>? actions) => new()
    {
        Name = "demo",
        Cwd = Path.GetTempPath(),
        Command = "cmd",
        Actions = actions,
    };
}
