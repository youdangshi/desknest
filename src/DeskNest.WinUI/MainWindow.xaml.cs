using System;
using System.Linq;
using DeskNest.WinUI.Pages;
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
