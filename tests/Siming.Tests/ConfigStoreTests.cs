using System.Text.Json;
using Siming.Core.Configuration;

namespace Siming.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "siming-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Load_CreatesEmptyConfig_WhenFileMissing()
    {
        var path = Path.Combine(_directory, "processes.json");
        using var store = new ConfigStore(path);

        var result = store.Load();

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(path));
        Assert.Empty(store.Current.Entries);
        Assert.Equal(200, store.Current.Settings.LogLines);
    }

    [Fact]
    public void Load_PreservesActionsAndUnknownFields_OnSave()
    {
        var path = Path.Combine(_directory, "processes.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "settings": { "logLines": 200, "startWithWindows": false },
              "entries": [
                {
                  "name": "demo",
                  "cwd": "D:\\demo",
                  "command": "npm run dev",
                  "actions": [
                    { "label": "跑测试", "type": "command", "command": "npm test" }
                  ],
                  "custom": "keep-me"
                }
              ]
            }
            """);

        using var store = new ConfigStore(path);
        var result = store.Load();

        Assert.True(result.Success, result.Error);
        var entry = Assert.Single(store.Current.Entries);
        Assert.NotNull(entry.Actions);
        Assert.Single(entry.Actions!);
        Assert.Equal("跑测试", entry.Actions![0].Label);
        Assert.True(entry.Extra!.ContainsKey("custom"));

        entry.Name = "demo2";
        Assert.True(store.TrySave(store.Current, out var error), error);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var saved = document.RootElement.GetProperty("entries")[0];
        Assert.Equal("demo2", saved.GetProperty("name").GetString());
        Assert.Equal("command", saved.GetProperty("actions")[0].GetProperty("type").GetString());
        Assert.Equal("keep-me", saved.GetProperty("custom").GetString());

        var json = File.ReadAllText(path);
        Assert.Contains("\n  \"version\"", json);
    }

    [Fact]
    public void TrySave_RejectsDuplicateNames()
    {
        var path = Path.Combine(_directory, "processes.json");
        using var store = new ConfigStore(path);
        store.Load();

        store.Current.Entries.Add(new EntryConfig { Name = "demo", Cwd = _directory, Command = "echo 1" });
        store.Current.Entries.Add(new EntryConfig { Name = "demo", Cwd = _directory, Command = "echo 2" });

        Assert.False(store.TrySave(store.Current, out var error));
        Assert.Contains("重复", error);
    }

    [Fact]
    public void Load_KeepsLastGoodConfig_WhenJsonBroken()
    {
        var path = Path.Combine(_directory, "processes.json");
        using var store = new ConfigStore(path);
        store.Load();
        store.Current.Entries.Add(new EntryConfig { Name = "good", Cwd = _directory, Command = "echo 1" });
        Assert.True(store.TrySave(store.Current, out _));

        File.WriteAllText(path, "{ not json");
        var result = store.Load();

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Single(store.Current.Entries);
        Assert.Equal("good", store.Current.Entries[0].Name);
    }

    [Fact]
    public void Save_DoesNotWriteComputedProperties_AndKeepsNullEnvValues()
    {
        var path = Path.Combine(_directory, "processes.json");
        using var store = new ConfigStore(path);
        store.Load();

        store.Current.Entries.Add(new EntryConfig
        {
            Name = "env",
            Cwd = _directory,
            Command = "echo 1",
            Env = new Dictionary<string, string?>
            {
                ["KEEP"] = "1",
                ["REMOVE"] = null,
            },
        });

        Assert.True(store.TrySave(store.Current, out var error), error);
        var json = File.ReadAllText(path);

        Assert.DoesNotContain("effectiveLogLines", json);
        Assert.DoesNotContain("shellKind", json);
        Assert.Contains("\"REMOVE\": null", json);
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
