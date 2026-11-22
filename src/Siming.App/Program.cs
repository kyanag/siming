using Siming.App.Mcp;
using Siming.Core.Configuration;
using Siming.Core.Platform;
using Siming.Core.Processes;

namespace Siming.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.RunAsync().GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        if (args.Any(argument => string.Equals(argument, "--mcp", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = McpServerHost.RunAsync().GetAwaiter().GetResult();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceNames.MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var signalEvent = EventWaitHandle.OpenExisting(SingleInstanceNames.ShowEventName);
                signalEvent.Set();
            }
            catch (Exception)
            {
                // 已有实例正在启动，忽略即可。
            }

            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                ShowFatal(exception);
            }
        };

        var configPath = Path.Combine(AppContext.BaseDirectory, "processes.json");
        using var store = new ConfigStore(configPath);
        var load = store.Load();
        if (!load.Success)
        {
            MessageBox.Show(
                $"配置文件有误：\n\n{load.Error}\n\n本次将使用空配置启动，修正后会自动重载。",
                "司命",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        EnsureEntryIds(store);

        using var supervisor = new ProcessSupervisor(store.Current);
        using var form = new MainForm(store, supervisor);
        var startHidden = args.Any(argument =>
            string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));

        using var context = new ApplicationContext { MainForm = form };
        if (startHidden)
        {
            // 强制创建句柄，让后台状态事件可以 BeginInvoke 回 UI 线程；不显示窗口。
            _ = form.Handle;
        }
        else
        {
            form.Show();
        }

        form.BeginAutoStart();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceNames.ShowEventName);
        var listener = new Thread(() =>
        {
            while (!form.IsDisposed)
            {
                if (!showEvent.WaitOne(500))
                {
                    continue;
                }

                try
                {
                    form.BeginInvoke(form.ShowFromTray);
                }
                catch (InvalidOperationException)
                {
                }
            }
        })
        {
            IsBackground = true,
            Name = "Siming.SingleInstanceListener",
        };
        listener.Start();

        Application.Run(context);
    }

    private static void ShowFatal(Exception exception)
    {
        try
        {
            MessageBox.Show(
                $"发生未处理的错误：\n\n{exception}",
                "司命",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>给缺少 id 的条目补一个稳定 id，改名时运行时身份不会变。</summary>
    private static void EnsureEntryIds(ConfigStore store)
    {
        var changed = false;
        foreach (var entry in store.Current.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }

        if (changed)
        {
            store.TrySave(store.Current, out _);
        }
    }
}
