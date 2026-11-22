using System.Reflection;
using System.Text.Json;
using Siming.App;
using Siming.Core.Configuration;

namespace Siming.Tests;

public sealed class EntryEditFormTests
{
    [Fact]
    public void Form_Constructs_ForNewAndExistingEntries()
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureVisualStyles();

                using var newEntry = new EntryEditForm(source: null, otherNames: ["other"]);
                using var existing = new EntryEditForm(
                    new EntryConfig
                    {
                        Id = "abc",
                        Name = "Blog Dev",
                        Cwd = Path.GetTempPath(),
                        Command = "npm run dev",
                        Shell = "cmd",
                        Encoding = "utf-8",
                        Env = new Dictionary<string, string?> { ["PORT"] = "3000", ["REMOVE"] = null },
                        Actions =
                        [
                            new ActionConfig { Label = "打开站点", Type = "open", Target = "http://localhost" },
                        ],
                    },
                    otherNames: ["other"]);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "窗体构造超时");
        Assert.Null(error);
    }

    [Fact]
    public void Save_PreservesIdActionsAndUnknownFields()
    {
        Exception? error = null;
        EntryConfig? result = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureVisualStyles();

                using var extra = JsonDocument.Parse("""{"custom":"keep-me"}""");
                var source = new EntryConfig
                {
                    Id = "stable-id",
                    Name = "old",
                    Cwd = Path.GetTempPath(),
                    Command = "echo old",
                    Shell = "cmd",
                    Encoding = "utf-8",
                    AutoStart = true,
                    Actions =
                    [
                        new ActionConfig { Label = "跑测试", Type = "command", Command = "npm test" },
                    ],
                    Extra = new Dictionary<string, JsonElement>
                    {
                        ["custom"] = extra.RootElement.GetProperty("custom").Clone(),
                    },
                };

                using var form = new EntryEditForm(source, otherNames: []);
                SetText(form, "_nameBox", "new name");
                SetText(form, "_commandBox", "echo new");

                var saveButton = GetField<Button>(form, "_saveButton");
                Assert.True(saveButton.Enabled);

                GetMethod(form, "SaveEntry").Invoke(form, null);
                result = form.Result;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "保存流程超时");
        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("stable-id", result!.Id);
        Assert.Equal("new name", result.Name);
        Assert.Equal("echo new", result.Command);
        Assert.NotNull(result.Actions);
        Assert.Equal("跑测试", result.Actions![0].Label);
        Assert.NotNull(result.Extra);
        Assert.Equal("keep-me", result.Extra!["custom"].GetString());

        // 「随司命启动」只在托盘菜单里切换，编辑对话框必须原样保留。
        Assert.True(result.AutoStart);
    }

    [Fact]
    public void Save_IsDisabled_WhenNameIsDuplicate()
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureVisualStyles();
                using var form = new EntryEditForm(
                    new EntryConfig
                    {
                        Name = "mine",
                        Cwd = Path.GetTempPath(),
                        Command = "echo 1",
                    },
                    otherNames: ["other"]);

                SetText(form, "_nameBox", "other");

                var saveButton = GetField<Button>(form, "_saveButton");
                var errorLabel = GetField<Label>(form, "_errorLabel");
                Assert.False(saveButton.Enabled);
                Assert.Contains("已存在", errorLabel.Text);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "校验流程超时");
        Assert.Null(error);
    }

    private static readonly object InitGate = new();
    private static bool _visualStylesInitialized;

    private static void EnsureVisualStyles()
    {
        lock (InitGate)
        {
            if (_visualStylesInitialized)
            {
                return;
            }

            Application.EnableVisualStyles();
            _visualStylesInitialized = true;
        }
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到字段 {name}");
        return (T)(field.GetValue(instance) ?? throw new InvalidOperationException($"字段 {name} 为空"));
    }

    private static MethodInfo GetMethod(object instance, string name) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"找不到方法 {name}");

    private static void SetText(object instance, string name, string value) =>
        GetField<TextBox>(instance, name).Text = value;
}
