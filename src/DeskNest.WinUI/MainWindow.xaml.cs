using System;
using System.Linq;
using DeskNest.WinUI.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace DeskNest.WinUI;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        App.Log("MainWindow ctor: begin");
        InitializeComponent();
        App.Log("MainWindow InitializeComponent: ok");
        try
        {
            ContentFrame.Navigate(typeof(OverviewPage));
            App.Log("Navigated to OverviewPage");
        }
        catch (Exception ex)
        {
            App.Log("Navigate FAILED: " + ex);
        }
        Title = "桌巢 DeskNest";
        ApplyLook();
        HookClose();
    }

    /// <summary>
    /// 关闭按钮 = 隐藏到托盘（托盘菜单「退出桌巢」才是真退出）。
    /// 用 AppWindow.Closing 可取消关闭；真退出时 App.ApplyingExit 会让这里放行。
    /// </summary>
    private void HookClose()
    {
        try
        {
            AppWindow.Closing += (_, e) =>
            {
                if (App.IsExiting) return;
                e.Cancel = true;
                AppWindow.Hide();
                App.Log("主窗口关闭按钮 → 隐藏到托盘");
            };
            App.Log("主窗口关闭已拦截（关窗隐藏到托盘）");
        }
        catch (Exception ex) { App.Log("拦截关闭失败：" + ex.Message); }
    }

    /// <summary>外观页改配色后由 App.ApplyLookToAllBoxes 调回来刷新。</summary>
    public void ApplyLook()
    {
        try { RootGrid.RequestedTheme = App.CurrentElementTheme; }
        catch (Exception ex) { App.Log("主窗口外观应用失败：" + ex.Message); }
    }

    /// <summary>按 Tag 切页（供其他页面跳转用，保持左侧选中项同步）。</summary>
    public void SelectPage(string tag)
    {
        try
        {
            foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            {
                if ((item.Tag as string) == tag) { Nav.SelectedItem = item; return; }
            }
            App.Log("SelectPage: 没找到 tag=" + tag);
        }
        catch (Exception ex) { App.Log("SelectPage 失败：" + ex.Message); }
    }

    /// <summary>供 App 的全局入口调用：聚焦当前搜索页输入框（若正在搜索页）。</summary>
    public void FocusSearchIfCurrent()
    {
        if (ContentFrame.Content is Pages.SearchPage page) page.FocusQuery();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag as string ?? "overview";
            var page = tag switch
            {
                "search" => typeof(SearchPage),
                "boxes" => typeof(BoxesPage),
                "organize" => typeof(OrganizePage),
                "appearance" => typeof(AppearancePage),
                "data" => typeof(DataPage),
                "about" => typeof(AboutPage),
                _ => typeof(OverviewPage)
            };
            if (ContentFrame.CurrentSourcePageType != page) ContentFrame.Navigate(page);
            App.Log("nav -> " + tag);
        }
        catch (Exception ex)
        {
            App.Log("nav FAILED (" + args.SelectedItem + "): " + ex);
        }
    }
}
