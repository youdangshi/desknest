using System.Windows;
using DeskNest.App.Host;
using DeskNest.App.Windows;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 界面冒烟测试：在 STA 线程里建一次 WPF Application + 主窗口，逐个打开所有已注册页面，
/// 任何一个页面构建抛异常都会让测试失败（相当于"把每个设置页都点一遍"）。
/// </summary>
public sealed class UiSmokeTests
{
    [Fact]
    public void EveryPage_BuildsWithoutError()
    {
        var failures = new List<string>();

        var thread = new Thread(() =>
        {
            try
            {
                // 只建对象，不启动索引/采样，避免测试期间占用资源
                AppServices.Initialize(startBackgroundWork: false);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                var window = new MainWindow();

                // 导航项必须唯一：早前版本出现过两个「桌宠」，这里做成回归断言
                var titles = window.PageTitles;
                if (titles.Count != titles.Distinct().Count())
                    failures.Add("导航存在重复标题：" + string.Join(" / ", titles));

                foreach (var key in window.PageKeys.ToList())
                {
                    window.NavigateTo(key);
                    if (window.LastPageError is { } error) failures.Add(error);
                }

                window.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                failures.Add("整体失败：" + ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "界面测试超时（可能卡在某个页面上）");

        Assert.True(failures.Count == 0, "以下页面构建失败：\n" + string.Join("\n", failures));
    }
}
