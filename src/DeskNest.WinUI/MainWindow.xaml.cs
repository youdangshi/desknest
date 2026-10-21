using System;
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
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag as string ?? "overview";
            var page = tag switch
            {
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
