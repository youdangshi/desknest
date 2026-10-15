using System.Windows;

namespace DeskNest.App.Ui;

/// <summary>
/// 设计令牌。数值对齐开源 DeskBox（GPL-3.0 项目，仅参考其设计度量，不使用其代码/SVG）
/// 的 SettingsOverviewResources.xaml 与 Fluent Design 规范：
///   卡片圆角 6、行最小高度 64、行内边距 16/10、图标与文字间距 20、
///   页面标题 28 SemiBold、设置项标题 14、说明 12、按钮 34 高 / 最小宽 104 / 圆角 6。
/// </summary>
public static class Tokens
{
    // 间距（Fluent 4px 网格）
    public const double Space2 = 2;
    public const double Space4 = 4;
    public const double Space6 = 6;
    public const double Space8 = 8;
    public const double Space12 = 12;
    public const double Space16 = 16;
    public const double Space20 = 20;
    public const double Space24 = 24;
    public const double Space28 = 28;
    public const double Space32 = 32;

    // 圆角
    public const double CornerCard = 6;
    public const double CornerControl = 6;
    public const double CornerSmall = 4;
    public const double CornerWidgetSmall = 8;
    public const double CornerWidgetMedium = 12;
    public const double CornerWidgetLarge = 16;

    // 尺寸
    public const double RowMinHeight = 64;
    public const double RowPaddingH = 16;
    public const double RowPaddingV = 10;
    public const double IconTextGap = 20;
    public const double IconSize = 20;
    public const double NavIconSize = 18;
    public const double ButtonHeight = 34;
    public const double ButtonMinWidth = 104;
    public const double IconButtonSize = 30;
    public const double ToggleWidth = 40;
    public const double ToggleHeight = 20;
    public const double ToggleThumb = 14;
    // 内容列宽：参考 DeskBox 在 1350px 宽窗口下约 810px 的正文列，避免行内控件被拉得太远
    public const double ContentMaxWidth = 840;

    public new static readonly Thickness RowPadding = new(RowPaddingH, RowPaddingV, RowPaddingH, RowPaddingV);
    public static readonly Thickness CardPadding = new(0, Space4, 0, Space4);
    public static readonly Thickness PageTitleMargin = new(0, 0, 0, Space4);

    // 字号
    public const double FontPageTitle = 28;
    public const double FontSubsectionTitle = 15;
    public const double FontSettingTitle = 14;
    public const double FontDescription = 12;
    public const double FontBody = 13;
    public const double FontSmall = 11;

    // 字体：正文用系统 UI 字体，图标用 Fluent 图标字体（Win11 自带；Win10 回退 MDL2）
    public const string UiFontFamily = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";
    public const string DisplayFontFamily = "Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI";
    public const string IconFontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";
}
