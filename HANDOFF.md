# 桌巢 DeskNest 交接快照

> 维护方式：**心跳自动化「桌巢交接快照（每天 9:30 / 21:30）」每天覆盖更新一次**；也可以手工改。给 dsh / VS Code 里的下一个 agent 看。
> 最近更新：2026-10-15 晚（手工初版，自动化已建）

## 一句话现状

**v1.1.2**：功能面已齐（桌面收纳 / 整理 / 全盘搜索 / 6 个组件 / 6 个可选模块 / 真 NavigationView 外壳 /
多显示器页 / 分级右键菜单 / 关于页检查更新，界面文案全中文，GitHub Release `v1.1.1` 带 71MB zip）。
本轮把**核心交互（拖动、缩放）重做**：盒子改走自己实现的手动增量拖动，并修掉 DPI 单位混用与
"跑出屏幕的盒子拉不回来"。测试 **106 项全绿**（含 18 项核心需求逐条体检）。
**待用户确认**：拖动/缩放是否已经好用（日志里有 `开始拖动窗口（手动）` 可查）。
文档：`docs/10-桌巢-完整软件策划书.md` + `docs/09-桌巢-完整软件方案书-v2.0.md`（20 张真实截图）。

## 最近改动

0. **v1.1.2（本轮，提交 `76ce620`）**：**核心交互修复**
   - 盒子拖动改为**手动增量拖动**：原来走"发 `WM_NCLBUTTONDOWN` 让系统拖"，而桌面层盒子实测是
     "被 WorkerW 拥有"的窗口，这条路会**静默失效却返回成功**，代码随即 `Handled=true` 提前返回，
     兜底的手动拖动永远轮不到 → 用户看到的就是"拖不动"。
   - 修 **DPI 单位混用**：`PointToScreen` 给物理像素、`Window.Left/Top` 是 DIP，125% 缩放下拖动会
     多跑 25%；缩放手柄同样有此问题，一并按 DPI 换算。
   - 拖动热区扩大：**盒子任意空白处都能拖**（以前只有 30px 标题栏），四边手柄 6px→10px、
     右下角 14px→20px 并画出可见手柄；被"锁定位置/尺寸"挡住时会弹提示，不再默默没反应。
   - **防跑丢改用物理像素判断**：实测有两个盒子停在屏幕外（物理 2840,3221 / 2777,3150）没被识别，
     因为拿 DIP 去比物理工作区；现在启动即拉回主屏并把新位置落盘。
   - 新增 `tests/DeskNest.Tests/CoreRequirementTests.cs`：**18 项核心需求逐条测试**
     （两种整理形态、回滚、拖入、缩放几何、几何持久化、盒组、规则、搜索索引与落盘、组件、
     两套外观独立与对比度、模块开关、计划关机、AI 授权、备份、删除还原）。

0. **v1.1.1（本轮）**：① 界面文案全中文（配色/材质/视图/触发方式/动作/桌宠层级这些下拉原来直接显示
   `FollowSystem`、`Acrylic` 这类英文枚举值，现在显示中文，设置文件里仍存原值）；
   ② 「关于」页新增检查更新（GitHub Releases，含"启动时自动检查"开关，默认关）；
   ③ 修「删除收纳盒删不掉」：删除逻辑抽成 `BoxService.Delete`（先还原文件 → 删记录 → **回读确认** → 每步写日志），
   确认框挂到主窗口，关盒子窗口前先 `DesktopLayer.Detach`（跨进程父子窗口直接销毁会留幽灵窗口）；
   ④ `Db.InTransaction`：索引落盘整段持锁，避免索引事务回滚时连带吞掉别的写入（删盒子/改设置）。

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

1. **等用户确认拖动 / 缩放已经好用**（本轮改的核心交互，只有在真机上拖一次才算验完）。
   如果还不行，看日志里有没有 `开始拖动窗口（手动）`：没有＝鼠标事件没送到盒子；有但没动＝位移没生效。
2. **重新跑一次完整全盘索引并压缩数据库**：当前库里是 34 万条的中间态（截图模式不建索引，
   之前几轮又都是跑到一半就退出），完整应为 300 万量级。
3. 设置页二级下钻 + 面包屑（15 页平铺，模块多了会显长）。
4. 桌面层「胶囊模式」。
5. 云同步（可选收费项）：先做加密快照导出/导入。
6. 把 `docs/09`、`docs/10` 的截图与正文在下次改动后重新生成一遍（`--shot-pages` 一条命令）。

## 未决问题 / 需要用户拍板

1. **拖动/缩放到底修好没有**——需要用户亲手拖一次（agent 无法替用户动鼠标）。
2. 是否现在就跑一次完整全盘索引（会占一段时间 CPU；跑完顺手压缩数据库，能退回约 1.5 GB 磁盘）。
3. 桌面盒子的默认透明度：用户当前把「收纳窗口」透明度调到了 0.3，截图里盒子比较淡，是否要在外观页给一组预设。
4. 是否把"跟随系统主题"真正接到 `UISettings.ColorValuesChanged`（现在只按时间近似 + 启动时读一次）。

## 环境与命令

```powershell
# 本机 .NET 10 SDK 装在私有目录（不污染系统）
$env:DOTNET_CLI_HOME="C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES="C:\Users\李\Desktop\项目组\.nuget"
$dn="C:\Users\李\.dotnet-sdk\dotnet.exe"
cd "C:\Users\李\Desktop\项目组\desktop-organizer"

& $dn build DeskNest.slnx
& $dn test  tests\DeskNest.Tests\DeskNest.Tests.csproj      # 必须在沙箱外跑
& $dn publish src\DeskNest.App\DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist\DeskNest-1.1.2

# 生成全部界面截图（文档用；会在结束时自动退出，不启动全盘索引）
& .\dist\DeskNest-1.1.0\DeskNest.exe --shot-pages .\_re\shots\pages
```

- 发布件：`dist\DeskNest-1.1.2\`（自包含；`docs/images` 里的截图来自 v1.1.1，改界面后要重新生成）
- 只读诊断脚本：`_re\shots\probe-windows.ps1` / `probe-children.ps1` / `probe-parent.ps1`
  （用 `-TargetPid <桌巢PID>` 打印窗口层级、父子关系、样式位——本轮就是靠它定位"拖不动"的）
  注意 `_re\` 在 `.gitignore` 里，**不会进仓库**：换机器/重新 clone 后这些脚本和图都得自己再生成
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
17. **桌面盒不是 `WS_CHILD` 子窗口**：实测 `SetParent` 到 WorkerW 之后，窗口是"**被 WorkerW 拥有**"的
    顶层窗口（`GetParent=0`、`gaParent=WorkerW`、`IsChild=false`、有 `WS_EX_LAYERED`）。
    所以：①"发 `WM_NCLBUTTONDOWN/HTCAPTION` 让系统拖"这条路对它**静默失效却返回成功**，
    别用；②`WindowMove.CanUseNativeDrag` 只能当参考（WPF 的窗口 `GetParent` 未必是 0）。
    另外注意：`new Window { ShowInTaskbar = false }` 在窗口生命周期里可能**重建 HWND**，
    `SetParent` 之后拿到的句柄未必还是同一个。
18. **`PointToScreen` 是物理像素，`Window.Left/Top` 是 DIP**：125% 缩放下直接相减会多跑 25%。
    拖动/缩放/防跑丢/多显示器排布全都要先统一单位（用 `VisualTreeHelper.GetDpi().DpiScaleX` 换算，
    或直接走 `WindowMove.MoveToPhysical` + `WindowShot.Bounds` 走物理像素）。
19. **测试里别改全局静态状态**：xunit 默认并行跑测试类，`Theme.ApplyShell/ApplyDesktop` 这类全局操作
    会互相打架（本轮踩到：外观独立性的测试被别的类改的主题带崩）。要么只做只读断言，要么串行化。
20. **写测试时用一个"错误的模型"会白忙**：本轮先按"盒是子窗口"写了断言，跑出来的数据对不上，
    靠 `_re\shots\probe-*.ps1` 打印真实窗口层级才确认它是"被拥有的顶层窗口"。
    遇到窗口层级/输入问题，先打印事实，再改代码。

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
| `src/DeskNest.App/Ui/WindowDrag.cs` | 拖动（手动增量 + DPI 换算）与缩放手柄（边 10px / 角 20px + 可见手柄） |
| `src/DeskNest.Native/Win32/WindowMove.cs` | Win32 拖动助手：`CanUseNativeDrag`、`MoveToPhysical`、`PhysicalBounds` |
| `src/DeskNest.Core/Services/BoxService.cs` | 删盒子：还原文件 → 删记录 → **回读确认** → 全程日志 |
| `src/DeskNest.Core/Services/UpdateService.cs` | 检查更新（GitHub Releases）+ `VersionCompare` 纯函数 |
| `src/DeskNest.App/Host/AppInfo.cs` | 版本号唯一来源（关于页与检查更新都读它，改版本只动 csproj 的 `<Version>`） |
| `tests/DeskNest.Tests/CoreRequirementTests.cs` | **核心需求逐条体检**（18 项，动核心功能后先跑它） |
| `tests/DeskNest.Tests/WindowDragPolicyTests.cs` | 拖动策略回归（手动拖动必须真的挪动窗口） |
| `src/DeskNest.App/Pages/PagesDisplays.cs` | 多显示器页（屏幕示意图 + 搬运 + 防跑丢） |
| `src/DeskNest.App/Host/PageShotter.cs` + `WallpaperBackdrop.cs` | `--shot-pages` 文档截图模式 |
| `src/DeskNest.Core/Services/SearchService.cs` | 全盘索引：扫描、落盘保护（不许用残缺索引覆盖完整索引） |
| `src/DeskNest.Core/Data/Repository.cs` | SQLite 仓储：盒子/条目/规则/索引落盘（含事务闸门） |
| `docs/09-*` | **当前对外交付的完整方案书 v2.0（含 20 张自动生成的真实截图）** |
| `docs/06-*` / `docs/07-*` / `docs/08-*` | 早期方案书 / 待确认界面方案 / 桌面交互规格与鼠标测试清单 |
