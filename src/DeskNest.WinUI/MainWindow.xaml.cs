using System;
using DeskNest.WinUI.Pages;
using Microsoft.UI.Xaml.Controls;

namespace DeskNest.WinUI;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        InitializeComponent();
        ContentFrame.Navigate(typeof(OverviewPage));
        Title = "桌巢 DeskNest";
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
    }
}
