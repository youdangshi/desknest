using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskNest.App.Ui;

/// <summary>
/// 图标系统：用 Fluent 图标字体（Segoe Fluent Icons / Win10 回退 Segoe MDL2 Assets）输出
/// **单色矢量字形**，替代之前的 emoji（emoji 是彩色位图，和界面观感割裂，就是"出戏"的来源）。
/// 码位沿用 Fluent/MDL2 规范，两个字体通用。
/// </summary>
public static class Icons
{
    // 导航与页面
    public const string Home = "\uE80F";
    public const string Folder = "\uE8B7";
    public const string Organize = "\uE74C";      // broom-ish（清理）
    public const string Widgets = "\uE71D";       // 组件/拼图感
    public const string Appearance = "\uE790";    // 调色板
    public const string Modules = "\uE71D";       // 模块（插件感）
    public const string Data = "\uE8F1";          // 存档
    public const string About = "\uE946";         // 信息
    public const string Pet = "\uE8D4";           // 宠物/动物
    public const string Image = "\uE8B9";         // 图片（出图）
    public const string Performance = "\uE9D9";   // 仪表
    public const string Usage = "\uE823";         // 时钟
    public const string Api = "\uE8C7";           // 卡片/额度
    public const string Power = "\uE7E8";         // 电源
    public const string Ai = "\uE945";            // 灯泡/智能

    // 行内小图标
    public const string Settings = "\uE713";
    public const string Add = "\uE710";
    public const string Delete = "\uE74D";
    public const string Edit = "\uE70F";
    public const string Refresh = "\uE72C";
    public const string Search = "\uE721";
    public const string Open = "\uE8E5";
    public const string FolderOpen = "\uE838";
    public const string Save = "\uE74E";
    public const string Undo = "\uE7A7";
    public const string Eye = "\uE7B3";
    public const string EyeHide = "\uED1A";
    public const string Star = "\uE735";
    public const string Tag = "\uE8EC";
    public const string Link = "\uE71B";
    public const string Globe = "\uE774";
    public const string Keyboard = "\uE765";
    public const string Calendar = "\uE787";
    public const string Clock = "\uE823";
    public const string Download = "\uE896";
    public const string Upload = "\uE898";
    public const string Cloud = "\uE753";
    public const string Chart = "\uE9D2";
    public const string Grid = "\uE80A";
    public const string List = "\uE8FD";
    public const string Color = "\uE790";
    public const string Lock = "\uE72E";
    public const string Unlock = "\uE785";
    public const string Pin = "\uE718";
    public const string Play = "\uE768";
    public const string Camera = "\uE722";
    public const string Microphone = "\uE720";
    public const string Bug = "\uEBE8";
    public const string Heart = "\uEB51";
    public const string Money = "\uE8C7";
    public const string Moon = "\uE708";
    public const string Sun = "\uE706";
    public const string Speed = "\uE9D9";
    public const string Zoom = "\uE71E";
    public const string Filter = "\uE71C";
    public const string Sort = "\uE8CB";
    public const string Copy = "\uE8C8";
    public const string Paste = "\uE77F";
    public const string Cut = "\uE8C6";
    public const string Reveal = "\uE8DA";
    public const string Check = "\uE73E";
    public const string Close = "\uE711";
    public const string More = "\uE712";
    public const string Up = "\uE70E";
    public const string Down = "\uE70D";
    public const string Right = "\uE76C";
    public const string Left = "\uE72B";
    public const string Expand = "\uE70D";
    public const string Collapse = "\uE70E";
    public const string Resize = "\uE7C4";
    public const string Erase = "\uE75C";
    public const string Rocket = "\uE7B5";
    public const string Save2 = "\uE792";
    public const string Note = "\uE70B";
    public const string Task = "\uE9D5";
    public const string Weather = "\uE9BD";
    public const string Music = "\uEC4F";
    public const string Monitor = "\uE7F4";

    /// <summary>
    /// 语义键 → (Fluent 图标名, 图标颜色)。颜色对齐 DeskBox 那种"每个导航项一个颜色"的做法：
    /// 彩色图标在白色卡片和浅色导航里辨识度更高，也比单色灰图标更有精神。
    /// </summary>
    public static (string Fluent, Color Tint) Pick(string glyphOrEmoji)
    {
        var key = glyphOrEmoji?.Trim() ?? "";
        return key switch
        {
            "🏠" or "家" or "home" => ("home", Rgb(0xE1, 0x9A, 0x2E)),
            "⭐" or "🌟" or "star" => ("star", Rgb(0xE1, 0x9A, 0x2E)),
            "🗂" or "📁" or "folder" => ("folder", Rgb(0x3A, 0x7B, 0xD5)),
            "📂" or "folder_open" => ("folder_open", Rgb(0x3A, 0x7B, 0xD5)),
            "🧹" or "broom" or "organize" => ("broom", Rgb(0x2A, 0xA1, 0x98)),
            "🧩" or "widgets" => ("apps_list", Rgb(0x3F, 0xA1, 0x5C)),
            "🎨" or "appearance" or "color" => ("paint_brush", Rgb(0x8E, 0x6F, 0xD8)),
            "🔌" or "modules" => ("box_multiple", Rgb(0x2A, 0xA1, 0x98)),
            "💾" or "🗄" or "data" => ("save", Rgb(0x5A, 0x6B, 0x7B)),
            "ℹ" or "about" => ("info", Rgb(0x3A, 0x7B, 0xD5)),
            "🐾" or "🎭" or "pet" => ("emoji", Rgb(0xE0, 0x6C, 0x8F)),
            "🖼" or "image" => ("image", Rgb(0x3A, 0x7B, 0xD5)),
            "📈" or "perf" or "gauge" => ("gauge", Rgb(0x7C, 0x6B, 0xD6)),
            "📊" or "chart" => ("data_bar_vertical", Rgb(0x7C, 0x6B, 0xD6)),
            "⏱" or "usage" or "clock" => ("clock", Rgb(0x2F, 0xA8, 0xCC)),
            "💰" or "💳" or "money" or "api" => ("money", Rgb(0xE1, 0x9A, 0x2E)),
            "⏻" or "power" => ("power", Rgb(0xE0, 0x5A, 0x5A)),
            "🔗" or "link" or "ai" => ("link", Rgb(0x7C, 0x6B, 0xD6)),
            "🔒" or "lock" => ("lock_closed", Rgb(0x5A, 0x6B, 0x7B)),
            "🔍" or "🔎" or "search" => ("search", Rgb(0x3A, 0x7B, 0xD5)),
            "🗑" or "🛑" or "delete" => ("delete", Rgb(0xE0, 0x5A, 0x5A)),
            "＋" or "🧲" or "🆕" or "add" => ("add", Rgb(0x3F, 0xA1, 0x5C)),
            "🌀" or "🔄" or "refresh" or "sync" => ("arrow_sync", Rgb(0x3A, 0x7B, 0xD5)),
            "⚠" or "warning" => ("warning", Rgb(0xE1, 0x9A, 0x2E)),
            "❓" or "help" => ("question_circle", Rgb(0x5A, 0x6B, 0x7B)),
            "✅" or "🎯" or "check" => ("checkmark_circle", Rgb(0x3F, 0xA1, 0x5C)),
            "👁" or "eye" => ("eye", Rgb(0x5A, 0x6B, 0x7B)),
            "💬" or "🔔" or "note" => ("document", Rgb(0x3A, 0x7B, 0xD5)),
            "🏷" or "tag" => ("tag", Rgb(0x2F, 0xA8, 0xCC)),
            "🖥" or "monitor" or "desktop" => ("desktop", Rgb(0x5A, 0x6B, 0x7B)),
            "⌨" or "keyboard" or "shortcut" => ("cursor_click", Rgb(0x2A, 0xA1, 0x98)),
            "💻" or "code" => ("apps_list", Rgb(0x5A, 0x6B, 0x7B)),
            "🖱" or "mouse" => ("cursor_click", Rgb(0x2A, 0xA1, 0x98)),
            "☁" or "cloud" => ("cloud", Rgb(0x3A, 0x7B, 0xD5)),
            "🚀" or "rocket" => ("rocket", Rgb(0x8E, 0x6F, 0xD8)),
            "🧪" or "test" => ("beaker", Rgb(0x2A, 0xA1, 0x98)),
            _ => ("settings", Rgb(0x5A, 0x6B, 0x7B))
        };
    }

    // 注意：类里有个字符串常量叫 Color，所以要写全名，否则 Color.FromRgb 会被解析到那个常量上
    private static System.Windows.Media.Color Rgb(byte r, byte g, byte b)
        => System.Windows.Media.Color.FromRgb(r, g, b);

    /// <summary>矢量图标（Fluent 路径 + 指定颜色）；没有对应矢量时回退到图标字体。</summary>
    public static FrameworkElement Vector(string fluentName, double size, Color tint)
    {
        var data = FluentIconData.Get(fluentName);
        if (data is null) return Glyph(Resolve(fluentName), size, new SolidColorBrush(tint));

        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Fill = new SolidColorBrush(tint),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var host = new Viewbox { Width = size, Height = size, Child = path, Stretch = Stretch.Uniform };
        return host;
    }

    /// <summary>按语义键直接拿矢量图标（自动配颜色）。</summary>
    public static FrameworkElement VectorFromKey(string glyphOrEmoji, double size)
    {
        var (fluent, tint) = Pick(glyphOrEmoji);
        return Vector(fluent, size, tint);
    }

    /// <summary>
    /// 语义键 → WPF-UI 的 Fluent 符号（SymbolRegular）。迁移到 WPF-UI 之后界面统一用它，
    /// 好处是图标与 DeskBox/系统同族、自动跟随主题前景色，也不用我们再维护 SVG 生成流程。
    /// 未收录的键回落到 Settings24（不会崩）。
    /// </summary>
    public static Wpf.Ui.Controls.SymbolRegular SymbolFor(string glyphOrEmoji)
    {
        var name = SymbolName(glyphOrEmoji);
        return Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(name, out var symbol)
            ? symbol
            : Wpf.Ui.Controls.SymbolRegular.Settings24;
    }

    /// <summary>语义键 → SymbolRegular 名字（含 24 尺寸后缀）。</summary>
    public static string SymbolName(string glyphOrEmoji) => (glyphOrEmoji ?? "").Trim() switch
    {
        "🏠" or "home" => "Home24",
        "⭐" or "star" => "Star24",
        "🗂" or "📁" or "folder" => "Folder24",
        "📂" or "folder_open" => "FolderOpen24",
        "🧹" or "broom" or "organize" => "Broom24",
        "🧩" or "widgets" => "AppsList24",
        "🧱" or "modules" or "🔌" => "BoxMultiple24",
        "🎨" or "appearance" or "color" => "PaintBrush24",
        "💾" or "🗄" or "data" => "Save24",
        "ℹ" or "about" => "Info24",
        "🐾" or "🎭" or "pet" => "Emoji24",
        "🖼" or "image" => "Image24",
        "📈" or "perf" or "gauge" => "Gauge24",
        "📊" or "chart" or "Σ" => "DataBarVertical24",
        "⏱" or "usage" or "clock" or "🕘" or "🕐" => "Clock24",
        "💰" or "💳" or "money" or "api" => "Money24",
        "⏻" or "power" => "Power24",
        "🔗" or "link" or "ai" => "Link24",
        "🔒" or "lock" => "LockClosed24",
        "🔍" or "🔎" or "search" => "Search24",
        "🗑" or "🛑" or "delete" => "Delete24",
        "＋" or "🧲" or "🆕" or "add" => "Add24",
        "🌀" or "🔄" or "refresh" or "sync" => "ArrowSync24",
        "⚠" or "warning" => "Warning24",
        "❓" or "help" => "QuestionCircle24",
        "✅" or "🎯" or "check" => "CheckmarkCircle24",
        "👁" or "eye" => "Eye24",
        "💬" or "🔔" or "note" or "🧾" => "Document24",
        "🏷" or "tag" => "Tag24",
        "🖥" or "monitor" or "desktop" => "Desktop24",
        "☁" or "cloud" => "Cloud24",
        "🚀" or "rocket" or "🤖" or "🧠" => "Rocket24",
        "🧪" or "test" => "Beaker24",
        "⌨" or "keyboard" or "shortcut" or "🖱" => "Keyboard24",
        "🌐" or "globe" => "Globe24",
        "📋" or "list" => "TextBulletListLtr24",
        "📌" or "pin" => "Pin24",
        "🎬" or "🎞" or "play" => "Play24",
        "🎲" or "sort" => "ArrowSort24",
        "📥" or "📦" or "download" => "ArrowDownload24",
        "📤" or "upload" => "ArrowUpload24",
        "🔑" or "key" => "Key24",
        "🔢" => "NumberSymbol24",
        "📏" or "📐" or "resize" => "Resize24",
        "⬜" or "▭" or "grid" => "SquareHint24",
        "🪟" or "window" => "Window24",
        "💧" or "drop" => "Drop24",
        "🌗" or "🌙" or "moon" => "DarkTheme24",
        "☀" or "sun" => "WeatherSunny24",
        "🚫" or "🧺" or "erase" => "Dismiss24",
        "✍" or "edit" => "Rename24",
        "🎞️" => "Play24",
        _ => "Settings24"
    };

    /// <summary>图标文本（单色，随前景色）。用于少量没有矢量资源的场合。</summary>
    public static TextBlock Glyph(string code, double size = Tokens.IconSize, Brush? foreground = null)
    {
        var block = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily(Tokens.IconFontFamily),
            FontSize = size,
            Foreground = foreground ?? Theme.Current.ForegroundBrush,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            // 图标字体在上方会留白，这里抵消一点基线偏移
            Margin = new Thickness(0, -1, 0, 0)
        };
        return block;
    }

    /// <summary>图标 + 文字的水平组合（导航项、按钮内容用）。</summary>
    public static StackPanel WithLabel(string code, string label, double iconSize = Tokens.NavIconSize,
        double gap = Tokens.Space12, Brush? foreground = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = Glyph(code, iconSize, foreground);
        glyph.Margin = new Thickness(0, -1, gap, 0);
        panel.Children.Add(glyph);
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = Tokens.FontBody,
            Foreground = foreground ?? Theme.Current.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    /// <summary>
    /// 把历史上写在页面里的 emoji 图标名映射成 Fluent 图标码位。
    /// 这样各页面不用逐处改代码，整个界面就从「彩色 emoji」切换到「单色矢量图标」。
    /// </summary>
    public static string Resolve(string glyphOrEmoji)
    {
        if (string.IsNullOrWhiteSpace(glyphOrEmoji)) return Settings;
        var first = glyphOrEmoji.TrimStart();
        if (first.Length >= 1 && first[0] >= '\uE000' && first[0] <= '\uF8FF') return first[0].ToString();
        return first switch
        {
            "🏠" => Home,
            "🗂" => Folder,
            "📁" => FolderOpen,
            "📂" => FolderOpen,
            "🧹" => Organize,
            "🧩" => Widgets,
            "🧱" => Modules,
            "🎨" => Appearance,
            "🔌" => Modules,
            "💾" => Data,
            "🗄" => Data,
            "ℹ" => About,
            "🐾" => Pet,
            "🎭" => Pet,
            "🖼" => Image,
            "📈" => Performance,
            "📊" => Chart,
            "⏱" => Usage,
            "⏳" => Clock,
            "💰" => Money,
            "💳" => Money,
            "⏻" => Power,
            "🔗" => Link,
            "🏷" => Tag,
            "👁" => Eye,
            "🔍" => Search,
            "🔎" => Search,
            "🔒" => Lock,
            "🔳" => Grid,
            "▭" => Grid,
            "⬜" => Grid,
            "🌫" => Color,
            "💧" => Color,
            "🪟" => Color,
            "🌗" => Moon,
            "🌙" => Moon,
            "🚀" => Rocket,
            "🔔" => Calendar,
            "🎯" => Check,
            "✅" => Check,
            "📌" => Pin,
            "🗑" => Delete,
            "🛑" => Close,
            "🧲" => Add,
            "🆕" => Add,
            "＋" => Add,
            "📥" => Download,
            "📤" => Upload,
            "📦" => Download,
            "🖥" => Monitor,
            "⌨" => Keyboard,
            "🤖" => Rocket,
            "🧠" => Rocket,
            "💬" => Note,
            "✍" => Edit,
            "🚫" => Erase,
            "🧺" => List,
            "🎲" => Sort,
            "🌀" => Refresh,
            "📋" => List,
            "🕘" => Clock,
            "🕐" => Clock,
            "🔑" => Save,
            "🔢" => Chart,
            "🌐" => Globe,
            "🧾" => Note,
            "🧰" => Bug,
            "🎬" => Play,
            "🎚" => Filter,
            "🎞" => Play,
            "📏" => Resize,
            "📐" => Resize,
            "Σ" => Chart,
            "▫" => Copy,
            _ => Settings
        };
    }

    /// <summary>给没有图标字体的极端情况兜底：返回能否渲染（用于诊断日志）。</summary>
    public static bool IconFontAvailable()
    {
        try
        {
            var families = Fonts.SystemFontFamilies.Select(f => f.Source).ToList();
            return families.Any(f => f.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase)
                                  || f.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
}
