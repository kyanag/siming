using Siming.Core.Configuration;
using Siming.Core.Control;

namespace Siming.Tests;

public sealed class EntryInputMapperTests
{
    [Fact]
    public void Apply_ForAdd_GeneratesStableDefaults()
    {
        var entry = EntryInputMapper.Apply(
            new EntryInput
            {
                Name = " demo ",
                Cwd = Path.GetTempPath(),
                Command = " echo 1 ",
            },
            existing: null);

        Assert.False(string.IsNullOrWhiteSpace(entry.Id));
        Assert.Equal("demo", entry.Name);
        Assert.Equal("echo 1", entry.Command);
        Assert.Equal("cmd", entry.Shell);
        Assert.Equal("utf-8", entry.Encoding);
        Assert.False(entry.AutoStart);
    }

    [Fact]
    public void Apply_ForUpdate_KeepsUnspecifiedFieldsAndActions()
    {
        var existing = new EntryConfig
        {
            Id = "stable",
            Name = "old",
            Cwd = Path.GetTempPath(),
            Command = "echo old",
            Shell = "powershell",
            Encoding = "gbk",
            AutoStart = true,
            Env = new Dictionary<string, string?> { ["PORT"] = "3000" },
            Actions =
            [
                new ActionConfig { Label = "跑测试", Type = "command", Command = "npm test" },
            ],
        };

        var updated = EntryInputMapper.Apply(
            new EntryInput { Command = "echo new" },
            existing);

        Assert.Equal("stable", updated.Id);
        Assert.Equal("old", updated.Name);
        Assert.Equal("echo new", updated.Command);
        Assert.Equal("powershell", updated.Shell);
        Assert.Equal("gbk", updated.Encoding);
        Assert.True(updated.AutoStart);
        Assert.Same(existing.Env, updated.Env);
        Assert.Same(existing.Actions, updated.Actions);
    }

    [Fact]
    public void Apply_RejectsEmptyRequiredFields()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EntryInputMapper.Apply(new EntryInput { Name = "", Cwd = Path.GetTempPath(), Command = "echo" }, null));
        Assert.Throws<InvalidOperationException>(() =>
            EntryInputMapper.Apply(new EntryInput { Name = "n", Cwd = "", Command = "echo" }, null));
        Assert.Throws<InvalidOperationException>(() =>
            EntryInputMapper.Apply(new EntryInput { Name = "n", Cwd = Path.GetTempPath(), Command = " " }, null));
    }
}
