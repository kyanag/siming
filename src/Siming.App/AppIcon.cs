using System.Drawing;

namespace Siming.App;

/// <summary>
/// 应用图标（任务栏、标题栏、Alt+Tab）。优先从程序集内嵌的多尺寸 app.ico 读取，
/// 这样 100%/200% DPI 下都能拿到合适的尺寸；失败时退回 exe 关联图标。
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "Siming.App.app.ico";

    private static Icon? _shared;

    public static Icon Shared => _shared ??= Load();

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            return new Icon(stream);
        }

        var executable = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executable))
        {
            var extracted = Icon.ExtractAssociatedIcon(executable);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
