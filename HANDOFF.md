# 桌巢 DeskNest 交接快照

> 维护方式：**心跳自动化「桌巢交接快照（每天 9:30 / 21:30）」每天覆盖更新一次**；也可以手工改。给 dsh / VS Code 里的下一个 agent 看。
> 最近更新：2026-10-15 晚（手工初版，自动化已建）

## 一句话现状

**v1.1.0**：界面骨架已换成 WPF-UI 的真 `NavigationView`（汉堡折叠、分组、页脚），新增「多显示器」页与
分级级联右键菜单，修掉 6 个真实缺陷，测试 **64 项全绿**；完整方案书见 `docs/09-桌巢-完整软件方案书-v2.0.md`
（含 20 张由 `--shot-pages` 自动生成的真实界面截图）。

## 最近改动

1. **真 NavigationView 外壳**：`MainWindow` 用 WPF-UI `NavigationView`（`PaneHeader` 放品牌头、`IsPaneToggleVisible`、
   `OpenPaneLength=232`、`CompactPaneLength=48`）。注意 4.3.0 的 `NavigationView` **没有 `Content` 属性**，
   右侧内容要等模板套用后从可视化树里取 `NavigationViewContentPresenter` 挂上去（在 `Loaded` 里做）。
2. **多显示器页**：`Monitors`（Win32 枚举，带工作区）→ `PagesDisplays.cs`；`DesktopWindows.ArrangeOn` 排布窗口、
   `RescueOffscreen` 把跑出屏幕的窗口拉回来；新建盒子落点跟随鼠标所在屏。
3. **分级右键菜单 + 标题栏样式**：`BoxWindow.Menu.cs` 重写（`Item` / `Sub` 两个工厂 + `ApplyMenuTheme`）；
   `BoxRecord.TitleStyle`（Standard / Compact / Floating / Hidden）；隐藏标题栏后 Alt+拖动移动。
4. **修 6 个真实缺陷**：图标映射（全是齿轮）、性能采样跨线程、索引保存事务嵌套、退出时释放库过早、
   残缺索引覆盖完整索引、数据库膨胀到 1.6 GB（新增「压缩数据库」）。
5. **文档截图模式**：`DeskNest.exe --shot-pages <目录>`（`Host/PageShotter.cs` + `WallpaperBackdrop.cs`），
   20 张截图落在 `_re/shots/pages/`。
6. **文档**：`docs/09-桌巢-完整软件方案书-v2.0.md`（含全部真实截图）。

## 下一步（按优先级）

1. **重新跑一次完整全盘索引并压缩数据库**：当前库里是 34 万条的中间态（截图模式不建索引，
   之前几轮又都是跑到一半就退出），完整应为 300 万量级。
2. 设置页二级下钻 + 面包屑（15 页平铺，模块多了会显长）。
3. 桌面层「胶囊模式」。
4. 云同步（可选收费项）：先做加密快照导出/导入。

## 未决问题 / 需要用户拍板

1. 是否现在就跑一次完整全盘索引（会占一段时间 CPU；跑完顺手压缩数据库，能退回约 1.5 GB 磁盘）。
2. 桌面盒子的默认透明度：用户当前把「收纳窗口」透明度调到了 0.3，截图里盒子比较淡，是否要在外观页给一组预设。
3. 是否把"跟随系统主题"真正接到 `UISettings.ColorValuesChanged`（现在只按时间近似 + 启动时读一次）。

## 环境与命令

```powershell
# 本机 .NET 10 SDK 装在私有目录（不污染系统）
$env:DOTNET_CLI_HOME="C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES="C:\Users\李\Desktop\项目组\.nuget"
$dn="C:\Users\李\.dotnet-sdk\dotnet.exe"
cd "C:\Users\李\Desktop\项目组\desktop-organizer"

& $dn build DeskNest.slnx
& $dn test  tests\DeskNest.Tests\DeskNest.Tests.csproj      # 必须在沙箱外跑
& $dn publish src\DeskNest.App\DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist\DeskNest-1.1.0

# 生成全部界面截图（文档用；会在结束时自动退出，不启动全盘索引）
& .\dist\DeskNest-1.1.0\DeskNest.exe --shot-pages .\_re\shots\pages
```

- 发布件：`dist\DeskNest-1.1.0\`（自包含）
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
11. **WPF-UI 4.3.0 的 `NavigationView` 没有 `Content` 属性**：右侧内容要在 `Loaded` 里从可视化树取
    `NavigationViewContentPresenter` 挂上去（它是私有成员，只能这么拿）；`SettingsCard` / `SettingsExpander`
    在 4.3.0 也不存在，只有 `CardControl`。
12. **桌面层窗口永远在普通窗口之下**：截图 / 演示时会被编辑器窗口盖住；要先 `DesktopLayer.Detach` 临时置顶，
    拍完 `DesktopLayer.Attach` 装回去。盒子的 `Left/Top` 在挂载前后都是屏幕坐标，摘挂不会跳位。
13. **`MenuItem.IsSubmenuOpen` 要等菜单完成布局才设得住**：同一个消息里直接设会被 WPF 回滚成 `false`，
    要用 `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`；`MenuItem.IsHighlighted` 的 setter 是 internal，别用。
14. **截图模式不要启动全盘索引**：索引跑到一半就被退出，会把残缺索引写进库（现已有保护，但没必要触发）。
15. **数据库会膨胀**：索引是整表重写，SQLite 不主动归还空闲页，实测涨到 1.6 GB。落盘后回收 WAL，
    必要时用「数据与备份 → 压缩数据库」（VACUUM）。
16. **GDI `CopyFromScreen` 抓的是屏幕上真实显示的画面**：目标窗口被别的窗口盖住就拍不到；
    抓之前要先确认它在最前（置顶 + 激活），或者别指望它会出现在截图里。

## 关键文件

| 文件 | 作用 |
| --- | --- |
| `src/DeskNest.App/Ui/DesignTokens.cs` | 设计令牌（间距/圆角/字号/内容列宽） |
| `src/DeskNest.App/Ui/ControlStyles.cs` | 控件模板（按钮/图标按钮/输入框/下拉/开关） |
| `src/DeskNest.App/Ui/Icons.cs` + `FluentIconData.cs` | 图标体系：语义键/emoji/**码位** → SymbolRegular（码位翻译别删，删了导航全变齿轮） |
| `src/DeskNest.App/Ui/Theme.cs` + `Contrast.cs` | 主题令牌与 WCAG 对比度计算 |
| `src/DeskNest.App/Ui/UiKit*.cs` | 设置页组件库（卡片/行/开关/信息条/可展开行/面包屑） |
| `src/DeskNest.App/Windows/MainWindow.cs` | 主窗口外壳（NavigationView + 顶部搜索 + 内容列限宽 + 页面注册表） |
| `src/DeskNest.App/Windows/BoxWindow.Menu.cs` | 盒子菜单（`Item`/`Sub` 两个工厂 + `ApplyMenuTheme`）与重命名/合并/剪贴板等动作 |
| `src/DeskNest.App/Windows/BoxWindow.cs` | 盒子窗口：标题样式、拖动、缩放、落盘限流 |
| `src/DeskNest.App/Pages/PagesDisplays.cs` | 多显示器页（屏幕示意图 + 搬运 + 防跑丢） |
| `src/DeskNest.App/Host/PageShotter.cs` + `WallpaperBackdrop.cs` | `--shot-pages` 文档截图模式 |
| `src/DeskNest.Core/Services/SearchService.cs` | 全盘索引：扫描、落盘保护（不许用残缺索引覆盖完整索引） |
| `src/DeskNest.Core/Data/Repository.cs` | SQLite 仓储：盒子/条目/规则/索引落盘（含事务闸门） |
| `docs/09-*` | **当前对外交付的完整方案书 v2.0（含 20 张自动生成的真实截图）** |
| `docs/06-*` / `docs/07-*` / `docs/08-*` | 早期方案书 / 待确认界面方案 / 桌面交互规格与鼠标测试清单 |
