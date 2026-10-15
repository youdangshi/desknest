using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

public static partial class ComfyUiPage
{
    /// <summary>最近出图的画廊：点击打开，右键可设为桌宠形象或删除。</summary>
    private static UIElement BuildGallery(MainWindow host, string outputDir)
    {
        var panel = new WrapPanel { Margin = new Thickness(6, 8, 6, 10) };
        List<string> files;
        try
        {
            files = Directory.Exists(outputDir)
                ? Directory.EnumerateFiles(outputDir, "*.png")
                    .Concat(Directory.EnumerateFiles(outputDir, "*.jpg"))
                    .OrderByDescending(File.GetLastWriteTime)
                    .Take(12)
                    .ToList()
                : new List<string>();
        }
        catch
        {
            files = new List<string>();
        }

        if (files.Count == 0)
        {
            panel.Children.Add(UiKit.Text("还没有出图。生成一次后，这里会显示最近的作品。", 12, secondary: true));
            return panel;
        }

        foreach (var file in files)
        {
            var captured = file;
            var border = new Border
            {
                Width = 148,
                Height = 170,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.Current.BorderBrush,
                Background = Theme.Current.CardBrush,
                Cursor = Cursors.Hand
            };
            var stack = new StackPanel();
            stack.Children.Add(BuildThumbnail(captured));
            var name = UiKit.Text(Path.GetFileName(captured), 10.5, secondary: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(6, 4, 6, 6);
            stack.Children.Add(name);
            border.Child = stack;
            border.MouseLeftButtonUp += (_, _) => OpenFile(captured);
            border.MouseRightButtonUp += (_, _) => ShowImageMenu(captured, host);
            panel.Children.Add(border);
        }
        return panel;
    }

    private static UIElement BuildThumbnail(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(file);
            bmp.DecodePixelWidth = 140;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Height = 132, Stretch = Stretch.UniformToFill };
        }
        catch
        {
            return UiKit.Text("（无法预览）", 11, secondary: true);
        }
    }

    private static void ShowImageMenu(string file, MainWindow host)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor("打开", () => OpenFile(file)));
        menu.Items.Add(MenuItemFor("在资源管理器中显示",
            () => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"")));
        menu.Items.Add(MenuItemFor("复制路径", () =>
        {
            try
            {
                Clipboard.SetText(file);
                Ui.Notifications.Toast("路径已复制", 2);
            }
            catch { }
        }));
        menu.Items.Add(MenuItemFor("设为桌宠形象", () => SetAsPetAvatar(file)));
        menu.Items.Add(MenuItemFor("删除这张图", () =>
        {
            try
            {
                File.Delete(file);
                host.RefreshCurrent();
            }
            catch (Exception ex)
            {
                Ui.Notifications.Toast("删除失败：" + ex.Message);
            }
        }));
        menu.IsOpen = true;
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static void SetAsPetAvatar(string file)
    {
        try
        {
            var dir = Path.Combine(AppPaths.PetsRoot, "from-comfy");
            Directory.CreateDirectory(dir);
            File.Copy(file, Path.Combine(dir, "portrait.png"), true);
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                "{\"format\":\"petpack/1\",\"id\":\"from-comfy\",\"name\":\"ComfyUI 出图\",\"author\":\"me\",\"license\":\"unknown\"}");
            var pet = AppServices.Repo.Pet();
            pet.PackId = "from-comfy";
            AppServices.Repo.SavePet(pet);
            Ui.Notifications.Toast("已设为桌宠形象（授权标为 unknown，请自行确认素材来源）", 5);
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("设置失败：" + ex.Message);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("打开失败：" + ex.Message);
        }
    }
}
