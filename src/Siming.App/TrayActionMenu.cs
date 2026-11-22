using Siming.Core.Configuration;
using Siming.Core.Extraction;
using Siming.Core.Processes;

namespace Siming.App;

/// <summary>
/// 条目子菜单底部的「其他 ▸」：结构由配置决定，可用性由运行时决定。
/// 拆成独立类是为了能脱离窗口做单元测试。
/// </summary>
internal static class TrayActionMenu
{
    public static ToolStripMenuItem? Build(
        EntryRuntime entry,
        Action<EntryRuntime, ActionKind, ActionConfig, string> execute)
    {
        var actions = entry.Config.Actions;
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        var more = new ToolStripMenuItem("其他");
        more.DropDown.ShowItemToolTips = true;

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Label))
            {
                continue;
            }

            var kind = ActionKinds.Parse(action.Type);
            var item = new ToolStripMenuItem(action.Label.Trim());

            if (ActionKinds.IsImplemented(kind) && TryResolvePayload(entry, kind, action, out var payload))
            {
                item.Click += (_, _) => execute(entry, kind, action, payload);
            }
            else
            {
                item.Enabled = false;
                item.ToolTipText = kind switch
                {
                    ActionKind.Unknown => $"未知的动作类型：{action.Type}",
                    ActionKind.Command => "尚未支持（v2-b）",
                    _ => "等待程序输出所需的值",
                };
            }

            more.DropDownItems.Add(item);
        }

        return more.DropDownItems.Count == 0 ? null : more;
    }

    internal static bool TryResolvePayload(
        EntryRuntime entry,
        ActionKind kind,
        ActionConfig action,
        out string payload)
    {
        var template = kind switch
        {
            ActionKind.Open => action.Target,
            ActionKind.Copy => action.Text,
            ActionKind.Command => action.Command,
            _ => null,
        };

        var builtins = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["cwd"] = entry.Config.Cwd,
            ["name"] = entry.Config.Name,
            ["id"] = entry.Config.Key,
            ["state"] = entry.State.ToString().ToLowerInvariant(),
            ["pid"] = entry.Pid?.ToString(),
        };

        // 提取值只在运行期有效：停止后即使内存里还有残留，也不允许被动作使用。
        Func<string, string?>? lookup = entry.IsRunning
            ? name => entry.Extractor.TryGetValue(name, out var value) ? value : null
            : null;

        return ActionTemplate.TryResolve(template, builtins, lookup, out payload);
    }
}
