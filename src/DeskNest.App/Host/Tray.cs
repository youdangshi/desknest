using System.Drawing;
using System.Windows.Forms;
using DeskNest.Core;

namespace DeskNest.App.Host;

/// <summary>系统托盘图标与菜单（不注册任何 Shell 右键菜单，只做托盘）。</summary>
public sealed class Tray : IDisposable
{
    private readonly NotifyIcon _icon;

    public Tray(Action toggleLayer, Action showMain, Action organize, Action search,
                Action toggleIcons, Action pauseAuto, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏 桌面盒与组件", null, (_, _) => toggleLayer());
        menu.Items.Add("快捷搜索（Alt+Space）", null, (_, _) => search());
        menu.Items.Add("一键整理桌面…", null, (_, _) => organize());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("显示/隐藏 桌面图标", null, (_, _) => toggleIcons());
        menu.Items.Add("暂停自动整理（1 小时）", null, (_, _) => pauseAuto());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开主窗口", null, (_, _) => showMain());
        menu.Items.Add("设置…", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出桌巢", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "桌巢 DeskNest",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) search();
        };
        _icon.DoubleClick += (_, _) => showMain();
        Log.Info("托盘图标已创建");
    }

    public void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try { _icon.ShowBalloonTip(4000, title, message, icon); } catch { }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
    }
}
