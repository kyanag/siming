using Siming.App.Mcp;
using Siming.Core.Control;

namespace Siming.Tests;

public sealed class McpConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "siming-mcp-tests",
        Guid.NewGuid().ToString("N"));

    public McpConfigTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void AddUpdateRemove_WorksWithoutRunningApp()
    {
        var paths = new McpPaths(_directory);
        var config = new McpConfig(paths);

        var added = config.Add(new EntryInput
        {
            Name = "MCP Test",
            Cwd = _directory,
            Command = "echo 1",
            Shell = "cmd",
        });

        Assert.Equal("unknown", added.State);
        Assert.True(File.Exists(paths.ConfigPath));

        var entries = config.ListStatuses();
        Assert.Single(entries);
        Assert.Equal("MCP Test", entries[0].Name);

        var updated = config.Update("MCP Test", new EntryInput { Command = "echo 2" });
        Assert.Equal("echo 2", updated.Command);
        Assert.Equal("MCP Test", updated.Name);
        Assert.Single(config.ListStatuses());

        config.Remove("MCP Test");
        Assert.Empty(config.ListStatuses());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
