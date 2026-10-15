# 桌巢 DeskNest 交接快照

> 维护方式：**心跳自动化「桌巢交接快照（每天 9:30 / 21:30）」每天覆盖更新一次**；也可以手工改。给 dsh / VS Code 里的下一个 agent 看。
> 最近更新：2026-10-15 晚（手工初版，自动化已建）

## 一句话现状

v1.0.0 已可运行并推送到 GitHub（`youdangshi/desknest`，main）；本轮修掉了"界面文字看不清"的主题缺陷，并补齐 Fluent 矢量图标体系与应用图标。**界面重做方案待用户三选一**（见 `docs/07-界面可读性修复与后续方案.md`）。

## 最近改动

1. **主题可读性修复（P0）**：`IsDark` 改为由配色方案决定（护眼/浅色永不为深色）；主窗口订阅 `Theme.Changed` 并重画外壳 + 导航 + 当前页；次要/辅助文字改用实色而非透明度叠加。
2. **对比度回归测试**：新增 `tests/DeskNest.Tests/ThemeContrastTests.cs`（9 项），四套配色下正文 ≥ 4.5:1、次要 ≥ 3:1；测试总数 **52 项全绿**。
3. **Fluent 矢量图标体系**：从微软 MIT 上游取 55 个图标 → 生成 `src/DeskNest.App/Ui/FluentIconData.cs`；`Icons` 提供"语义键 → 图标 + 颜色"映射。
4. **应用图标**：矢量绘制后打包 7 尺寸 `Assets/desknest.ico`（16/24/32/48/64/128/256），接到 exe / 窗口 / 托盘；生成脚本 `_re/make_icon.ps1`。
5. **UI 组件库**：`Section` / `Row` / `Toggle` / `Banner`（信息条）/ `ExpanderRow`（可展开行）/ `Breadcrumb` / `TitleWithHelp` / `NavGroup` + `NavButton`；`ControlStyles` 用真实 `ControlTemplate` 替换 WPF 默认外观。
6. **文档**：`docs/06-桌巢-完整软件方案书-v1.1.md`（对外交付版，11 章）、`docs/07-界面可读性修复与后续方案.md`（待确认方案）、`THIRD-PARTY-NOTICES.md`。

## 下一步（按优先级）

1. **等用户选界面方案**：A 保守修复 / B 引入 lepoco/wpfui（MIT，最接近 WinUI3 质感）/ C 自绘 Fluent 外壳。默认按"先 A 后 C"推进。
2. 修剩余 P2 观感问题：底部标题裁切、自绘细滚动条、长说明换行时行内图标居中。
3. **ComfyUI 素材生成**：`H:\comfyui` 已确认（有 `juggernautXL_v9Rdphoto2Lightning`）；当前**未运行**，需用户选"谁来启动"。产出：桌宠五态、空态插画 3、纹理 4、横幅 2。
4. 多显示器管理页（可视化显示器示意图 + 每屏数量 + 全部移到此显示器）。
5. 全盘索引优化：增量落盘、内存上限（当前一次索引 326 万条，落盘时有一次较大写事务）。

## 未决问题 / 需要用户拍板

1. 界面走 A / B / C 哪个方案。
2. ComfyUI 由用户启动（推荐，绘世一键）还是由 agent 直接起 `main.py`（可能影响之后绘世启动、需先结束进程）。
3. 素材清单是否增删。
4. 是否把 `Desktop.Icon`-style 的主题跟随系统真正接到 `UISettings.ColorValuesChanged`（现在是按时间近似 + 启动时读取）。

## 环境与命令

```powershell
# 本机 .NET 10 SDK 装在私有目录（不污染系统）
$env:DOTNET_CLI_HOME="C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES="C:\Users\李\Desktop\项目组\.nuget"
$dn="C:\Users\李\.dotnet-sdk\dotnet.exe"
cd "C:\Users\李\Desktop\项目组\desktop-organizer"

& $dn build DeskNest.slnx
& $dn test  tests\DeskNest.Tests\DeskNest.Tests.csproj      # 必须在沙箱外跑
& $dn publish src\DeskNest.App\DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist\DeskNest-1.0.0
```

- 发布件：`dist\DeskNest-1.0.0\`（自包含）+ `dist\DeskNest-1.0.0-win-x64.zip`
- 数据目录：`%LOCALAPPDATA%\DeskNest`（可用 `DESKNEST_DATA_DIR` 覆盖）
- 推送：`pwsh -File tools\push-github.ps1 -Token <PAT> -Owner youdangshi -Repo desknest`（走代理 `127.0.0.1:7897`）

## 已知坑（踩过的，别再踩）

1. **`App.xaml` 里不能写中文注释** → XAML 编译器报 `MC1000: Value cannot be null`。注释用英文。
2. **`powershell.exe -File` 按 ANSI 读 .ps1** → 脚本里不能出现中文字面量；需要中文路径就**用参数传**（如 `_re/make_icon.ps1 -Out <路径>`）。
3. **`dotnet test` 必须在沙箱外跑**：测试要写用户桌面、`%LOCALAPPDATA%`、监听本地端口、创建 WPF 窗口。
4. **改了 csproj 会触发 NuGet 还原** → 沙箱内连不上 nuget.org，编译要在沙箱外做一次。
5. **App 项目同时用 WPF 与 WinForms（托盘）** → csproj 里要 `Using Remove="System.Windows.Forms"` / `System.Drawing`，否则 `Button/Brush/Color/Point` 大量冲突；`System.IO` 等要用 `GlobalUsings.cs` 补。
6. **`dotnet new sln` 在 .NET 10 生成 `.slnx`**，命令里要写 `DeskNest.slnx`。
7. **模板自带的 `MainWindow.xaml(.cs)` 必须删掉**，否则与 `DeskNest.App.Windows.MainWindow` 撞名。
8. **自包含发布时模块 dll 在 RID 子目录**（`bin\Release\net10.0-windows\win-x64\`），复制模块的 MSBuild 目标要两条路径兜底。
9. **git 推送**：本机直连 GitHub 不通，必须 `-c http.proxy=http://127.0.0.1:7897`；目录属主与沙箱用户不同时先加 `safe.directory` 例外。
10. **图标/Logo 不用图像生成模型**：按 `imagegen` 技能自身规则，图标这类要匹配现有图标体系、需多尺寸清晰的素材应走矢量（我们就是这样做的）。

## 关键文件

| 文件 | 作用 |
| --- | --- |
| `src/DeskNest.App/Ui/DesignTokens.cs` | 设计令牌（间距/圆角/字号/内容列宽） |
| `src/DeskNest.App/Ui/ControlStyles.cs` | 控件模板（按钮/图标按钮/输入框/下拉/开关） |
| `src/DeskNest.App/Ui/Icons.cs` + `FluentIconData.cs` | 图标体系与 55 个 Fluent 矢量图标 |
| `src/DeskNest.App/Ui/Theme.cs` + `Contrast.cs` | 主题令牌与 WCAG 对比度计算 |
| `src/DeskNest.App/Ui/UiKit*.cs` | 设置页组件库（卡片/行/开关/信息条/可展开行/面包屑） |
| `src/DeskNest.App/Windows/MainWindow.cs` | 主窗口外壳（导航分组 + 顶部搜索 + 内容列限宽） |
| `docs/06-*` / `docs/07-*` | 完整方案书 / 待确认的界面方案 |
