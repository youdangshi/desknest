# 桌巢 DeskNest 交接快照

> 维护方式：**手工更新**（原「每天 9:30 / 21:30 自动覆盖」的钩子已按用户要求于 2026-10-16 停用，项目改由 dsh 接手维护）。
> 这份快照在 2026-10-16 晚为「把项目移交给 dsh」全面整理过一次；之后谁改动谁更新，别指望自动兜底。
> dsh 接手请**先读 `HANDOFF-dsh.md`**（专用接手须知：第一步做什么、两个待修缺陷的根因、红线与隐私）。
> 最近更新：2026-10-16 21:26（手工整理） · 当前 HEAD：`8cd7521`（已推 GitHub main）

## 一句话现状

**功能面已经齐了**：桌面收纳盒（新建 / 拖入 / 拖出 / 缩放 / 吸附 / 合并成组 / 解散回桌面）、整理与规则（预览 + 回滚）、
全盘搜索、6 个桌面组件（含照 DeskBox 重做的待办两页）、模块开关、多显示器页、分级右键菜单、关于页检查更新。
代码在 `main` 上、**已推 GitHub**（`youdangshi/desknest`），冒烟验证器 **40 项全过**，`dotnet test` 在本机跑不起来（见已知坑 3）。

**当前卡在两件用户能看见的事上**（本轮已定位根因，都写进「下一步」）：
① 盒子右键菜单/盒子上的字用户说「根本看不清」（实测不是对比度问题，是字号与行距）；
② **改「外观与主题」页的参数，盒子的颜色根本不变**（材质 / 材质强度这两个控件在画盒子时根本没被读取 → 死控件）。

## 最近改动（新的在上）

1. `8cd7521` **（2026-10-16 晚）菜单可读性 + 新建收纳盒不再顺手搬文件**
   - 菜单文字色改为「按菜单自己的底色算对比度」（`MenuTextBrush`），不再直接用主题前景色——两套配色不一致时会撞成深底深字；
     字号 13 → **14**、每行高 30、内边距放宽。附证据：对用户截图做像素采样，底色 `#28282E` + 近白字 `#F5F5F5` 实测对比度 **12:1**，
     所以「看不清」的主因是 1080p 上中文小字太挤太糊，这一轮动的是字号/行距（**待用户肉眼确认**）。
   - 「新建」区块拆成两个入口：**「新建收纳盒」（只建空盒子，绝不移动任何文件）** 与「把桌面文件收进来 → 去整理」（引导到整理页先预览）。
   - 新建盒子弹窗重写：类型可选（普通盒 / 映射盒）、显示去向、内容可滚动、**按钮固定在底部**（以前内容长了按钮被窗口边切掉半截）。
2. `9e02cc0` → `2e5a13e` **修「自动收纳后文件找不到」（严重）与「页面加载失败」**
   - 桌面分区模式搬进存储目录的文件**没有登记进盒子**（`OrganizeEngine.Execute` 只在指定 BoxId 时才登记）→ 界面看不到、无法还原。
     已加 `BoxService.IsUnderManagedRoot` + `OrganizeEngine.EnsureCategoryBox`，按分类自动建盒并登记；用户被搬走的 20 个文件已全部还回桌面。
   - 待办新增的 `color/is_starred/steps_json` 三列在 ALTER TABLE 时没给默认值 → 老记录是 NULL，读取时当整数读直接抛异常（整页打不开）。已按可空读 + 补列带 `DEFAULT`。
   - 新增「**解散盒子（盒内文件全部回到桌面）**」`BoxService.DissolveToDesktop`；新增拖动**吸附对齐** `WindowSnap`（吸别的盒子/屏幕工作区，阈值 8px）；
     盒子标题栏加极淡底色 + 按钮改完整前景色 + 描边。
3. `fdc3616` **待办界面照 DeskBox 截图重做**：列表页（全部/今天/重要/已完成 四页签带数量 + 8 色筛选 + 添加 + 清除已完成）
   与编辑页（标题、截止/提醒/重复/颜色四个胶囊、步骤子任务、Markdown 备注、顶部 返回/保存/收藏/删除）。
4. `810f080` **组件（性能/待办等）也能拖动合并**：同盒子规则（悬停高亮 → 松手合并 → 滚轮切组）。
5. `fd365eb` **拖动/缩放期间强制置顶**，松手按原设置还原（只"提到最上层"不够，鼠标划过别的程序就会被重新盖住）。
6. `bb7ca4b` **待办/随记的输入框真的能打字了**：根因是盒子窗口带 `WS_EX_NOACTIVATE`（点它不抢焦点）拿不到键盘焦点。
7. `76ce620` **核心交互重做**：盒子改走自己实现的**手动增量拖动**（"让系统去拖"对这类窗口静默失效却返回成功）、修 DPI 单位混用（125% 缩放下多跑 25%）、
   缩放手柄加大、跑出屏幕的盒子拉回来；新增 18 项核心需求逐条测试。

## 下一步（按优先级）

1. **修「改外观参数，盒子颜色根本不变」（用户已连续两轮反馈，最该先做）**
   根因（已读代码确认，不是猜）：盒子/组件的底色只由 `MakeBackground()`（`src/DeskNest.App/Windows/BoxWindow.cs` 196–203 行）
   与 `WidgetWindow.ApplyLook()`（`src/DeskNest.App/Windows/WidgetWindow.cs` 160–172 行）决定，而它们**只用了 `t.Background` + `t.Opacity`，
   完全没读 `Material` / `MaterialIntensity`**；`t.Background` 又只随「配色方案（深色/浅色/纯黑/护眼）」变，**产品里根本没有"卡片颜色"这个入口**。
   所以外观页上「材质」「材质强度」两个控件对盒子是**死控件**（拖了完全没反应），「配色」一改就是整页翻底色——用户感受就是"颜色根本不变"。
   建议改法：① `ThemeTokens` 加 `CardColor` 覆盖项 + 外观页加「卡片颜色」取色器（含几个预设）；② 让材质强度真正参与绘制；
   ③ 盒子菜单「更多操作」里补「材质 / 材质强度 / 颜色」（现在只有 透明度 / 圆角 / 样式作用域）；④ 冒烟验证器加断言：改颜色后 `MakeBackground` 的 RGB 必须真的变。
2. **请用户指认"看不清"的具体位置**：菜单？盒子里文件名？标题按钮？（本轮菜单已放大，但"盒内文件名/按钮"仍可能有各自的问题）
   建议顺手做一个全局「界面文字大小」（13/14/15）+ 高对比开关，一次覆盖菜单、盒子标题、文件名。
3. **待办到点提醒没接**：`PetEngine.RemindTodo`（`src/DeskNest.Core/Services/PetEngine.cs` 90 行）目前只被 `PetWindow.cs` 222 行的演示调用，
   没有定时器按 `DueAt - RemindMinutesBefore` 触发；`Repeat`（每天/每周/每月/农历）也不会自动生成下一条。
4. **设置页二级下钻 + 面包屑**（现在 15 页平铺，模块多了会显长）。
5. **重跑一次完整全盘索引并压缩数据库**（库里是 34 万条中间态，完整应为 300 万量级；跑完 VACUUM 能退回约 1.5 GB 磁盘）。
6. **版本号统一**：App csproj `<Version>` 是 `1.1.0`，而 GitHub Release / 文档里出现过 `v1.1.1`、旧交接里出现过 `v1.1.2`。版本唯一来源是 `src/DeskNest.App/DeskNest.App.csproj`。

## 未决问题 / 需要用户拍板

1. **「看不清」到底指哪一处**——需要用户截图圈一下（agent 看不到用户的眼睛，实测对比度已经达标）。
2. **卡片颜色做到什么程度**：用户要「可统一所有盒子，也可单独控制某一个」，需要确认是加「全局卡片色 + 每盒覆盖」还是只做每盒。
3. **是否现在就跑完整全盘索引**（会占一段时间 CPU；跑完附带压缩数据库，能退回约 1.5 GB 磁盘）。
4. 桌面盒子默认透明度：用户当前把「收纳窗口」透明度调到 0.3，截图里盒子比较淡，是否给一组预设。
5. 是否把"跟随系统主题"真正接到 `UISettings.ColorValuesChanged`（现在只按时间近似 + 启动时读一次）。
6. **拖动 / 缩放现在到底好不好用**——需要用户亲手拖一次才算验完（日志里搜 `开始拖动窗口（手动）`）。

## 环境与命令

```powershell
# 本机 .NET 10 SDK 装在私有目录（不污染系统）
$env:DOTNET_CLI_HOME="C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES="C:\Users\李\Desktop\项目组\.nuget"
$dn="C:\Users\李\.dotnet-sdk\dotnet.exe"
cd "C:\Users\李\Desktop\项目组\desktop-organizer"

& $dn build DeskNest.slnx                                   # 编译（改过 csproj 会触发还原，要在沙箱外跑一次）
& $dn test  tests\DeskNest.Tests\DeskNest.Tests.csproj      # 本机报 testhost Access Denied，跑不通（见已知坑 3）

# 冒烟验证器：当前唯一可信的自动化验收手段（40 项断言，替代 dotnet test）
& $dn build tools\DeskNest.Smoke\DeskNest.Smoke.csproj -c Release
& $dn exec "tools\DeskNest.Smoke\bin\Release\net10.0-windows\DeskNest.Smoke.dll"

# 跑起来看界面（必须用签名宿主；直接跑 exe 会弹「无法验证发布者」）
Start-Process -FilePath $dn -ArgumentList @("exec","src\DeskNest.App\bin\Debug\net10.0-windows\DeskNest.dll") -WorkingDirectory "C:\Users\李\Desktop\项目组\desktop-organizer"

# 发布自包含版
& $dn publish src\DeskNest.App\DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist\DeskNest-1.1.0

# 生成全部界面截图（文档用；结束自动退出，不启动全盘索引）
& .\dist\DeskNest-1.1.0\DeskNest.exe --shot-pages .\_re\shots\pages
```

- **推送**：`git -c http.proxy=http://127.0.0.1:7897 -c https.proxy=http://127.0.0.1:7897 push "https://<PAT>@github.com/youdangshi/desknest.git" main`
  （本机直连 GitHub 不通；PAT 在用户给过的那条消息里，**不要写进任何进仓库的文件**）
- 只读诊断脚本：`_re\shots\probe-windows.ps1` / `probe-children.ps1` / `probe-parent.ps1`（`-TargetPid <桌巢PID>` 打印窗口层级/样式位）
  `_re\shots\ui-auto.ps1` 带 list/rect/state/hit/click/rclick/key 等子命令，但**这台机器上注入的鼠标键盘到不了应用**（`SendInput` 返回成功但无效），
  只当"列窗口 / 读矩形 / 命中测试"用。`_re\` 在 `.gitignore` 里，**不进仓库**，换机器要自己再生成。
- 数据目录：`%LOCALAPPDATA%\DeskNest`（可用环境变量 `DESKNEST_DATA_DIR` 覆盖）
- **定时钩子已全部停用**（2026-10-16，用户要求）：原 `doc-sync-10-22`（策划书同步）与 `9-30-21-30`（本文件快照）都不再运行。
  文档同步与截图重出请按 `CONTEXT.md` 里的「改完就同步策划书」约定手工做（`--shot-pages` + `docs: 策划书同步（要点）` 提交）。

## 已知坑（踩过的，别再踩）

1. **`App.xaml` 里不能写中文注释** → XAML 编译器报 `MC1000: Value cannot be null`。注释用英文。
2. **`powershell.exe -File` 按 ANSI 读 .ps1** → 脚本里不能出现中文字面量；需要中文路径就**用参数传**（如 `_re/make_icon.ps1 -Out <路径>`）。
3. **`dotnet test` 在本机跑不起来**：testhost 报 `Access Denied`（环境问题，不是测试代码）。改完核心功能**跑 `tools/DeskNest.Smoke`**（40 项断言）当验收，
   测试代码仍留在 `tests/DeskNest.Tests`（91 个测试方法），换台机器能跑起来就一起跑。
4. **改了 csproj 会触发 NuGet 还原** → 沙箱内连不上 nuget.org，编译要在沙箱外做一次。
5. **App 项目同时用 WPF 与 WinForms（托盘）** → csproj 里要 `Using Remove="System.Windows.Forms"` / `System.Drawing`，否则 `Button/Brush/Color/Point` 大量冲突；`System.IO` 等要用 `GlobalUsings.cs` 补。
6. **`dotnet new sln` 在 .NET 10 生成 `.slnx`**，命令里要写 `DeskNest.slnx`。
7. **模板自带的 `MainWindow.xaml(.cs)` 必须删掉**，否则与 `DeskNest.App.Windows.MainWindow` 撞名。
8. **自包含发布时模块 dll 在 RID 子目录**（`bin\Release\net10.0-windows\win-x64\`），复制模块的 MSBuild 目标要两条路径兜底。
9. **git 推送**：本机直连 GitHub 不通，必须 `-c http.proxy=http://127.0.0.1:7897`；目录属主与沙箱用户不同时先加 `safe.directory` 例外。
10. **图标/Logo 不用图像生成模型**：按 `imagegen` 技能自身规则，图标这类要匹配现有图标体系、需多尺寸清晰的素材应走矢量（我们就是这样做的）。
11. **WPF-UI 4.3.0 的 `NavigationView` 没有 `Content` 属性**：右侧内容要在 `Loaded` 里从可视化树取
    `NavigationViewContentPresenter` 挂上去（它是私有成员，只能这么拿）；`SettingsCard` / `SettingsExpander` 在 4.3.0 也不存在，只有 `CardControl`。
12. **盒子现在是"可激活的普通窗口"，不是桌面层窗口**（历史结论已过时，注意别按旧认知改代码）：
    早期想把盒子 `SetParent` 挂到 Explorer 的 `WorkerW`（壁纸之上、图标之下），实测在这台机器上**挂上去就变成"看不见也点不到"**；
    现在 `DesktopLayer.Attach` 直接 `return false`（旧实现保留在 `AttachToDesktopLayerLegacy`），盒子能点、能拖、点击置顶、拖动期间强制 Topmost。
    代价：其他程序会盖住盒子，Win+D 也会把它一起收走（见 `docs/10` 的 13.2）。
13. **`MenuItem.IsSubmenuOpen` 要等菜单完成布局才设得住**：同一个消息里直接设会被 WPF 回滚成 `false`，
    要用 `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`；`MenuItem.IsHighlighted` 的 setter 是 internal，别用。
14. **截图模式不要启动全盘索引**：索引跑到一半就被退出，会把残缺索引写进库（现已有保护，但没必要触发）。
15. **数据库会膨胀**：索引是整表重写，SQLite 不主动归还空闲页，实测涨到 1.6 GB。落盘后回收 WAL，
    必要时用「数据与备份 → 压缩数据库」（VACUUM）。
16. **GDI `CopyFromScreen` 抓的是屏幕上真实显示的画面**：目标窗口被别的窗口盖住就拍不到；
    抓之前要先确认它在最前（置顶 + 激活），或者别指望它会出现在截图里。
17. **"发 `WM_NCLBUTTONDOWN/HTCAPTION` 让系统拖"对盒子静默失效却返回成功**：所以拖动一律走 `WindowDrag` 的手动增量实现，别改回去。
    另外：`WindowMove.CanUseNativeDrag` 只能当参考；`new Window { ShowInTaskbar = false }` 在窗口生命周期里可能**重建 HWND**。
18. **`PointToScreen` 是物理像素，`Window.Left/Top` 是 DIP**：125% 缩放下直接相减会多跑 25%。
    拖动/缩放/防跑丢/多显示器排布/吸附对齐全都要先统一单位（用 `VisualTreeHelper.GetDpi().DpiScaleX` 换算，或走 `WindowMove.MoveToPhysical` + `WindowShot.Bounds` 的物理像素）。
19. **测试里别改全局静态状态**：xunit 默认并行跑测试类，`Theme.ApplyShell/ApplyDesktop` 这类全局操作会互相打架
    （本轮踩到：外观独立性的测试被别的类改的主题带崩）。要么只做只读断言，要么串行化。
20. **写测试时用一个"错误的模型"会白忙**：曾按"盒是子窗口"写断言，数据对不上；靠 `_re\shots\probe-*.ps1` 打印真实窗口层级才确认。
    遇到窗口层级/输入问题，**先打印事实，再改代码**。
21. **菜单配色要改对地方**：`BoxWindow.Menu.cs` 里 `Item()` 工厂设的颜色会被 `PaintItems()` 在菜单打开时**整表覆盖**——
    只改 `Item()` 等于白改（本轮踩到过）。真要换色，改 `ApplyMenuTheme()`（底色 + 文字成对设定）与 `PaintItems()`；
    子菜单是独立 Popup、**不继承**父菜单配色，每一项都得自己带上底色与前景色。
    WPF 菜单模板用的是 `{StaticResource SystemColors.MenuBrushKey}` 这类**静态**引用，在控件实例上覆盖无效，必须写进 `Application.Resources`（见 `Theme.ApplyMenuColors`）。
22. **判断"看不清"别靠肉眼**：对截图做像素采样算真实对比度（本轮测出 `#28282E` 底 + `#F5F5F5` 字 = 12:1，达标 → 问题在字号/行距）。
    采样脚本用 `System.Drawing` + `LockBits`（逐像素 `GetPixel` 慢到几分钟）。
23. **外观参数"没反应"要先看它到底有没有被读**：`Theme.ResolveDesktop(...)` 收下了 `Material` / `MaterialIntensity`，但绘制时没用 → 死控件。
    改外观类需求时，从"最终画出来的那个函数"（`MakeBackground` / `ApplyLook`）倒着查，比从设置页正着查快。

## 关键文件

| 文件 | 作用 |
| --- | --- |
| `HANDOFF-dsh.md` | **交给 dsh 的接手须知（先读这个）**：第一步做什么、待修缺陷根因、红线与隐私、文档索引 |
| `CONTEXT.md` | 跨工具共享的「进行中工作 / 已定决策 / 约定」（追加式，别重写别人的内容） |
| `src/DeskNest.App/Ui/Theme.cs` + `Contrast.cs` | 主题令牌（两套：设计窗口 / 收纳窗口）与 WCAG 对比度计算 |
| `src/DeskNest.App/Ui/DesignTokens.cs` | 设计令牌（间距/圆角/字号/内容列宽） |
| `src/DeskNest.App/Ui/ControlStyles.cs` | 控件模板（按钮/图标按钮/输入框/下拉/开关） |
| `src/DeskNest.App/Ui/Icons.cs` + `FluentIconData.cs` | 图标体系：语义键/emoji/**码位** → SymbolRegular（码位翻译别删，删了导航全变齿轮） |
| `src/DeskNest.App/Ui/UiKit*.cs` | 设置页组件库（卡片/行/开关/信息条/图表/面包屑） |
| `src/DeskNest.App/Windows/MainWindow.cs` | 主窗口外壳（NavigationView + 顶部搜索 + 内容列限宽 + 页面注册表） |
| `src/DeskNest.App/Windows/BoxWindow.cs` | 盒子窗口：**底色绘制 `MakeBackground`**、标题样式、拖动/缩放、双击隐藏、点击置顶 |
| `src/DeskNest.App/Windows/BoxWindow.Menu.cs` | 盒子分级菜单（`Item`/`Sub`/`PaintItems`/`ApplyMenuTheme`）与重命名/合并/剪贴板等动作 |
| `src/DeskNest.App/Windows/WidgetWindow.cs` + `WidgetWindow.Todo.cs` + `WidgetWindow.Content.cs` | 桌面组件与待办两页界面 |
| `src/DeskNest.App/Ui/WindowDrag.cs` | 手动增量拖动（DPI 换算 + 吸附 + 拖动期 Topmost）与缩放手柄 |
| `src/DeskNest.App/Pages/PagesBoxes.cs` | 收纳盒页 + 新建盒子弹窗（只建空盒） |
| `src/DeskNest.App/Pages/PagesSettings.cs` | 外观与主题（两套令牌 + 复制/统一）、各设置页 |
| `src/DeskNest.Core/Services/BoxService.cs` | 删盒子（还原→删记录→回读确认）、解散回桌面、合并盒子与组件 |
| `src/DeskNest.Core/Services/OrganizeEngine.cs` | 整理引擎（预览/执行/回滚、`EnsureCategoryBox`） |
| `src/DeskNest.Core/Services/WindowSnap.cs` | 吸附对齐纯函数（阈值 8px） |
| `src/DeskNest.Core/Services/PetEngine.cs` | 桌宠状态机（`RemindTodo` 目前只有演示调用） |
| `src/DeskNest.Core/Services/SearchService.cs` | 全盘索引（扫描 + 落盘保护） |
| `src/DeskNest.Core/Data/Repository.cs` | SQLite 仓储（含事务闸门、列迁移 `EnsureColumn`） |
| `tools/DeskNest.Smoke/Program.cs` | **冒烟验证器（40 项断言）——当前唯一自动化验收手段** |
| `tests/DeskNest.Tests/CoreRequirementTests.cs` | 核心需求逐条体检（18 项，动核心功能后先跑它） |
| `docs/10-桌巢-完整软件策划书.md` | **完整策划书**（含核心功能验收记录 20 条 + 缺陷现象/根因/处理） |
| `docs/09-桌巢-完整软件方案书-v2.0.md` | 逐页截图版方案书（20 张真实截图） |
| `docs/08-桌面交互规格与鼠标测试清单.md` | 桌面交互规格（每一层鼠标动作该有什么反应） |
