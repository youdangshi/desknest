namespace DeskNest.App.Ui;

/// <summary>
/// 下拉选项的"值 → 中文标签"对照表。
/// 值保持和设置文件、代码里的枚举名完全一致（改了会读不出老设置），
/// 界面上则一律显示中文——否则配色、材质这些地方会冒出一片英文。
/// </summary>
public static class UiOptions
{
    public static readonly (string Value, string Label)[] Schemes =
    {
        ("FollowSystem", "跟随系统"),
        ("Dark", "深色"),
        ("Light", "浅色"),
        ("PureBlack", "纯黑"),
        ("EyeCare", "护眼")
    };

    public static readonly (string Value, string Label)[] Materials =
    {
        ("Acrylic", "亚克力"),
        ("Mica", "云母"),
        ("Solid", "纯色")
    };

    public static readonly (string Value, string Label)[] ViewModes =
    {
        ("Icon", "图标"),
        ("List", "列表")
    };

    public static readonly (string Value, string Label)[] PowerActions =
    {
        ("Shutdown", "关机"),
        ("Restart", "重启"),
        ("Sleep", "睡眠")
    };

    public static readonly (string Value, string Label)[] PowerTriggers =
    {
        ("Clock", "定时"),
        ("Countdown", "倒计时"),
        ("Idle", "空闲后")
    };

    public static readonly (string Value, string Label)[] PetLayers =
    {
        ("Always", "总在最前"),
        ("OnDesktop", "贴桌面层"),
        ("WhenIdle", "空闲才出现")
    };
}
