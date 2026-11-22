using System.Text.Json;
using Siming.Core.Configuration;
using Siming.Core.Extraction;
using Siming.Core.Processes;

namespace Siming.Tests;

public sealed class ExtractionTests
{
    [Fact]
    public void First_Mode_FreezesAfterFirstMatch()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["url"] = new() { Regex = @"listening on (http://\S+)" },
        });

        extractor.OnLine("server listening on http://127.0.0.1:4096");
        extractor.OnLine("server listening on http://127.0.0.1:5000");

        Assert.True(extractor.TryGetValue("url", out var url));
        Assert.Equal("http://127.0.0.1:4096", url);
    }

    [Fact]
    public void Latest_Mode_KeepsNewest()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["url"] = new() { Regex = @"listening on (http://\S+)", Mode = ExtractModes.Latest },
        });

        extractor.OnLine("server listening on http://127.0.0.1:4096");
        extractor.OnLine("server listening on http://127.0.0.1:5000");

        Assert.True(extractor.TryGetValue("url", out var url));
        Assert.Equal("http://127.0.0.1:5000", url);
    }

    [Fact]
    public void NonMatchingAndEmptyCaptures_DoNotOverwrite()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["password"] = new() { Regex = @"server password (.*)$", Secret = true },
        });

        extractor.OnLine("server password mjQ8nrfXzGJS2KXTXo0tpbIt");
        extractor.OnLine("server password ");

        Assert.True(extractor.TryGetValue("password", out var password));
        Assert.Equal("mjQ8nrfXzGJS2KXTXo0tpbIt", password);
    }

    [Fact]
    public void InvalidRegex_IsReportedButDoesNotThrow()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["broken"] = new() { Regex = "(" },
        });

        extractor.OnLine("anything");

        Assert.NotNull(extractor.ConfigurationError);
        Assert.False(extractor.TryGetValue("broken", out _));
    }

    [Fact]
    public void Reset_ClearsValuesAndUnfreezes()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["url"] = new() { Regex = @"listening on (http://\S+)" },
        });

        extractor.OnLine("listening on http://127.0.0.1:4096");
        extractor.Reset();

        Assert.False(extractor.TryGetValue("url", out _));

        extractor.OnLine("listening on http://127.0.0.1:5000");

        Assert.True(extractor.TryGetValue("url", out var url));
        Assert.Equal("http://127.0.0.1:5000", url);
    }

    [Fact]
    public void Reconfigure_KeepsUnchangedField_AndResetsChangedField()
    {
        var extractor = new LogExtractor(new Dictionary<string, ExtractRule>
        {
            ["url"] = new() { Regex = @"listening on (http://\S+)" },
            ["password"] = new() { Regex = @"password (\S+)" },
        });

        extractor.OnLine("listening on http://127.0.0.1:4096");
        extractor.OnLine("password abc");

        extractor.Reconfigure(new Dictionary<string, ExtractRule>
        {
            ["url"] = new() { Regex = @"listening on (http://\S+)" },
            ["password"] = new() { Regex = @"secret (\S+)" },
        });

        Assert.True(extractor.TryGetValue("url", out var url));
        Assert.Equal("http://127.0.0.1:4096", url);
        Assert.False(extractor.TryGetValue("password", out _));

        extractor.OnLine("secret xyz");

        Assert.True(extractor.TryGetValue("password", out var password));
        Assert.Equal("xyz", password);
    }

    [Fact]
    public void Entry_ExtractsFromItsOwnLog()
    {
        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Name = "opencode",
                    Cwd = Path.GetTempPath(),
                    Command = "opencode serve",
                    Extract = new Dictionary<string, ExtractRule>
                    {
                        ["url"] = new() { Regex = @"server listening on (https?://\S+)" },
                    },
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(config);
        var entry = supervisor.Snapshot()[0];

        entry.Log.Add("server listening on http://127.0.0.1:4096");

        Assert.True(entry.Extractor.TryGetValue("url", out var url));
        Assert.Equal("http://127.0.0.1:4096", url);
    }

    [Fact]
    public void Template_JoinsExtractedValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["cwd"] = @"D:\demo",
            ["name"] = "demo",
            ["pid"] = "1234",
        };

        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "127.0.0.1",
            ["port"] = "4096",
        };

        var ok = ActionTemplate.TryResolve(
            "http://${host}:${port}",
            values,
            name => extracted.TryGetValue(name, out var value) ? value : null,
            out var result);

        Assert.True(ok);
        Assert.Equal("http://127.0.0.1:4096", result);
    }

    [Fact]
    public void Template_DisablesWhenAnyValueIsMissing()
    {
        var values = new Dictionary<string, string?> { ["name"] = "demo" };
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "127.0.0.1",
        };

        var ok = ActionTemplate.TryResolve(
            "http://${host}:${port}",
            values,
            name => extracted.TryGetValue(name, out var value) ? value : null,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public async Task Supervisor_ExtractsFromRealProcessOutput_AndClearsOnStop()
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
                },
            ],
        };

        using var supervisor = new ProcessSupervisor(
            config,
            gracefulStopTimeout: TimeSpan.FromMilliseconds(400),
            forceStopTimeout: TimeSpan.FromSeconds(2));

        var entry = supervisor.Snapshot()[0];
        await supervisor.StartAsync(entry);

        await WaitAsync(() => entry.Extractor.TryGetValue("url", out _), TimeSpan.FromSeconds(8));

        Assert.True(
            entry.Extractor.TryGetValue("url", out var url),
            string.Join("\n", entry.Log.Snapshot()));
        Assert.Equal("http://127.0.0.1:4096", url);

        await supervisor.StopAsync(entry);

        Assert.Equal(EntryState.Stopped, entry.State);
        Assert.False(entry.Extractor.TryGetValue("url", out _));
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    public void Config_RoundTripsShorthandAndOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), "siming-tests", Guid.NewGuid().ToString("N"), "processes.json");

        var config = new AppConfig
        {
            Entries =
            [
                new EntryConfig
                {
                    Id = "opencode",
                    Name = "opencode",
                    Cwd = Path.GetTempPath(),
                    Command = "opencode serve",
                    Extract = new Dictionary<string, ExtractRule>
                    {
                        ["url"] = new() { Regex = @"server listening on (https?://\S+)" },
                        ["password"] = new()
                        {
                            Regex = @"server password (\S+)",
                            Secret = true,
                            Mode = ExtractModes.Latest,
                        },
                    },
                },
            ],
        };

        using (var store = new ConfigStore(path))
        {
            Assert.True(store.TrySave(config, out var error), error);
        }

        var json = File.ReadAllText(path);
        Assert.Contains(@"""url"": ""server listening on (https?://\\S+)""", json);

        using (var store = new ConfigStore(path))
        {
            var result = store.Load();
            Assert.True(result.Success, result.Error);

            var extract = store.Current.Entries[0].Extract!;
            Assert.Equal(@"server listening on (https?://\S+)", extract["url"].Regex);
            Assert.True(extract["password"].Secret);
            Assert.True(extract["password"].IsLatest);
        }
    }
}
