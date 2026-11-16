# ADR-002：桌面层 UI 迁移到 WinUI 3

- 日期：2026-10-19
- 状态：**已决定**（项目负责人拍板），处于 P0 可行性验证阶段
- 影响面：所有界面代码；`DeskNest.Core` 与 `DeskNest.Native` 基本不动

## 1. 决定

桌面壳从 **WPF** 迁移到 **WinUI 3（Windows App SDK）**。

## 2. 为什么

| 起因 | 说明 |
| --- | --- |
| 界面反复返工 | WPF 不自带 Fluent 控件，卡片、下拉、按钮、开关全部手搓；每搓一处就漏一处 —— 用户先后报了「白底白字」「对比度不够」「按钮不像设计的一部分」三次同类问题 |
| 参照对象就是 WinUI | DeskBox 用 C# + WinUI 3，原生 Fluent，控件自带正确的深浅色与对比度 |
| 合规已解决 | 项目已切 GPL-3.0（docs/25），可以正大光明参考/复用 DeskBox 的 WinUI 实现 |

## 3. 不换的部分（约占现有工作量 60%）

| 模块 | 处理 |
| --- | --- |
| `DeskNest.Core`（SQLite、Repository、FileOps、OrganizeEngine、BoxService、Settings、Rules…） | **原样保留**，与 UI 框架无关 |
| `DeskNest.Native`（Win32 封装：owner 窗口、WorkerW、DPI、拖动、热键） | **保留复用**；WinUI 3 窗口同样需要这套 P/Invoke |
| `tools/DeskNest.Smoke`（82 项断言） | **原样保留** —— 这是迁移期防止功能漂移的唯一硬保障 |
| 文档、许可、打包脚本 | 保留 |

## 4. 要重写的部分

| 部分 | 说明 |
| --- | --- |
| 主窗口 + 导航 | `NavigationView` + `Frame`，5 个页面 |
| 5 个设置页 | XAML + code-behind，取代现在的 C# 手搭 UI |
| 桌面盒窗口 | WinUI `Window` + 自绘标题栏；组选择器复用 `Ui/GroupSelector` 的纯逻辑 |
| 组件窗口 / 桌宠 | 同上 |
| 托盘、通知、右键菜单 | 走 WinUI/Win32 菜单，样式统一 |
| 主题 | 从「自己搓令牌」改成「映射到 WinUI 主题资源 + 少量覆盖」 |

## 5. 分阶段（每阶段都要能跑、能看、能验收）

| 阶段 | 目标 | 完成判据 | 预计 |
| --- | --- | --- | --- |
| **P0 可行性**（当前） | 证明 WinUI 3 窗口**能贴到桌面层、能点、不进 Alt-Tab** | 一个空白 WinUI 窗口挂在壁纸层上，点击有响应、Alt+Tab 里看不到 | 1 天 |
| **P1 壳** | 主窗口 + NavigationView + 5 个空页能切 | 5 页标题正确、深浅色切换不错乱 | 3–4 晚 |
| **P2 桌面盒** | 盒子窗口 + 文件列表 + 组选择器 | 拖入文件、切换组成员、卷起都正常 | 5–6 晚 |
| **P3 设置页** | 5 页真实内容迁移 | 与现 WPF 版逐页对照，功能不缺 | 4–5 晚 |
| **P4 外围** | 托盘、热键、通知、菜单 | 与现版一致 | 2–3 晚 |
| **P5 打包** | 自带运行时单文件 exe | 换台没装 .NET 的机器也能双击运行 | 1–2 晚 |

合计约 **3 周**（按每晚 1–2 小时）。

## 6. 风险与对策

| 风险 | 等级 | 对策 |
| --- | --- | --- |
| 桌面层宿主做不出来（贴不到壁纸、点不动、被 Alt+Tab 收录） | **高** | 先做 P0 spike；DeskBox 已验证可行（owner 窗口 + SetParent/Win32 组合），做不出来就**停止迁移**，回滚成本≈0 |
| Windows App Runtime 分发（用户机器要装运行时或自带） | 中 | P5 做 self-contained；打包体积约 120–180MB，可接受 |
| 迁移期功能漂移（新旧两版行为不一致） | 中 | 沿用 82 项冒烟（Core 层）+ 每阶段截图对照 |
| 沙箱内无法 restore/编译 WinUI 包 | 低 | 首次 `restore-winui.cmd` 由用户在本机跑一次，之后可离线 `--no-restore` 编译 |

## 7. 回退方案

- 迁移期间 **WPF 版保持可编译、可打包、可发版**（现有 `dist` 包不受影响）；
- 只要 P0 或 P1 出现"修不动"的坑，立刻停在 spike，把精力放回 WPF 版；
- 仓库里两个 UI 项目并存，`DeskNest.App`（WPF）在新版本到 P3 之前是唯一可发布版本。

## 8. 复用 DeskBox 的合规要求（GPL-3.0）

1. 复用的文件头写 `// 源自 DeskBox (GPL-3.0) https://github.com/Tianyu199509/DeskBox，已修改：…`；
2. 纯方案借鉴（不复制代码）在本文档或 docs/26 注明即可；
3. 分发时源码随包提供（GPL 义务，见 docs/25）。

## 9. 当前状态

- [x] 决策与文档（本文）
- [ ] **P0 spike**：`spike-winui/Spike.csproj` 已建；等首次 NuGet 还原（`restore-winui.cmd`）完成后验证"贴桌面层"
- [ ] P1 壳（未开始）

---

## 10. P0 结果（2026-10-19 22:17）：**失败，建议停止迁移**

三次在真实桌面环境（非沙箱）运行的日志，内容都只有一行：

```
=== spike start mode=owner ===
=== spike start mode=workerw ===
=== spike start mode=bottom ===
```

`Main` 进去了、日志写了，随后在 `Application.Start()` 里崩掉 —— 窗口从未出现，也没有 `CLICK` 记录。

我这边（沙箱）跑同一份 exe 得到更明确的栈：

```
Fatal error. 0xC0000005
at WinRT.Interop.Platform.RoGetActivationFactory(...)
at Microsoft.UI.Xaml.Application.get__objRef_global__Microsoft_UI_Xaml_IApplicationStatics()
```

即 **WinUI 3 的 XAML 运行时激活失败**：`RoGetActivationFactory("Microsoft.UI.Xaml.Application")` 拿不到激活工厂。已排除的原因：

| 怀疑点 | 结果 |
| --- | --- |
| 缺 Windows App Runtime | 已改 self-contained（自带 6 个 WindowsAppRuntime dll、508 文件、214MB），仍然失败 |
| MakePri / resources.pri | 已用命令行 `-p:MicrosoftProjectPriGenerationEnable=false` 彻底关掉，干净重建 0 错误 |
| 代码/托管入口 | 日志证明 `Main` 已执行，问题在这之后 |
| 三种宿主方案 | 三种都到不了调用宿主代码那一步（崩在窗口创建前） |

剩下未验证的方向（都不便宜）：打包成 MSIX 并安装（需要证书 + 安装步骤）、undocked RegFree WinRT 的 manifest 定制、换 Windows App SDK 版本重试。

### 结论

以「每晚 1–2 小时」的节奏，继续赌环境的收益比已经很低：**连"最小 WinUI 3 窗口"都起不来，谈不上贴桌面层**。按预定的判断标准，P0 判失败。

### 替代路线（按性价比排序）

| 路线 | 做什么 | 成本 | 我的自查能力 |
| --- | --- | --- | --- |
| **B. WPF 界面层重写**（推荐） | 保留 Core/Native/Smoke，把 `Ui/` 与 `Pages/` 按「令牌 + 组件库」重写一遍，观感对齐 WinUI 3 Fluent（WPF-UI 本来就是 WinUI 的 WPF 移植） | 1–2 周 | **完整**：出图 + 逐像素量对比度，不需要用户参与 |
| C. Avalonia 重写 | 换跨平台框架，窗口同样是 HWND，现有 Win32 宿主层可复用 | 2–3 周 | 部分（沙箱内可编译，运行仍需用户） |
| D. 继续 WinUI 3 | 打 MSIX、调 manifest、试不同 SDK 版本 | 未知（≥3 轮用户参与） | 几乎为零 |

### 10.1 补记（22:35）：给 exe 加 assemblyIdentity 后，连日志都不写了

- 无 assemblyIdentity：`Main` 能进（日志 1 行），崩在 `Application.Start()` → `0xC0000005`（reg-free WinRT 清单被整份忽略）。
- 补 assemblyIdentity 后：进程**在 Main 之前就退出**，连第一行日志都没有 —— 说明新增清单让 SxS 装载失败，比之前更早死。

两个方向都堵住，说明这不是"再调一个属性"能解决的事，而是**这条工具链在本机（中文路径 / 受限环境）没有可用组合**。另有一个未验证但成本极低的假设：`MakePri.exe` 的 `0x80070005` 可能来自**非 ASCII 路径**（Windows SDK 工具在某些版本对中文路径处理有缺陷）。验证方式是把探针工程放到纯 ASCII 路径（如 `C:\DeskNestWinUI`）再编译一次。

### 10.2 建议（22:35 之后）

1. 今晚**停止** WinUI 折腾，把精力放回可用产品；
2. 明天若要继续 C 路线，先做那个 ASCII 路径验证（一次性，纯编译，不需要运行）；
3. 若只是想"界面像 WinUI"，A 路线（WPF 界面层重写）在观感上等价、成本可控、且我全程能自查。

### 10.3 补充证据（2026-10-20）：MakePri 的中文路径假设被否

- 直接运行 SDK 里的 **x64** `makepri.exe /?` → **退出码 0**，正常打印用法。
  → 说明「中文路径导致 MakePri 拒绝访问」不成立。
- 之前我抓到的 `0x80070005` 来自 `...\bin\10.0.26100.0\**arm64**\makepri.exe`（我自己 glob 取错了架构），
  MSBuild 实际调用的是哪一份**未确认**（重建被 NuGet TLS 挡住，没跑到 PRI 那一步）。
- 结论不变：这只影响「能不能打包 PRI」，不解释 `RoGetActivationFactory` 失败；WinUI 3 仍处 P0 失败状态。

### 10.4 路线决定（2026-10-20）

改走 **A：不换框架、重写界面层**，方案见 `docs/29-桌巢-界面层重写方案.md`。WinUI 3 迁移暂停，保留全部证据与探针工程。

### 10.5 P0 结果（2026-10-20 19:52）：**通过，`owner` 方案可用**

关键突破：**装成 MSIX 包（开发者模式松散注册）后 WinUI 3 一次起来**，此前所有崩溃都是「免安装 exe 的 SxS/清单」路线问题，不是 WinUI 本身。

探针日志（三种方案逐个点）：

```
HWND=0x221058
[启动时]  owner=0x0     parent=0x0      exstyle=0x100          toolwindow=False
[owner]   owner=0x10232 parent=0x0      exstyle=0x180          toolwindow=True     ← owner 关系建立，窗口仍可点
[workerw] SetParent -> 65552(旧父窗口)，但随后 parent=0x0，窗口变小    ← 不稳定
[bottom]  owner=0x10232 parent=0x0      exstyle=0x8000180      toolwindow=True noactivate=True  ← 沉底后看不见
```

用户现场观察与日志一致：

| 方案 | 现象 | 结论 |
| --- | --- | --- |
| `owner` | 窗口仍在、**还能点** | ✅ 可用（与 WPF 版同一思路：把壁纸窗口设为 owner） |
| `workerw` | 窗口**变小**（子窗口坐标被重算） | ⚠️ 可用但需自己做坐标换算，暂不采用 |
| `bottom` | 窗口**看不见**（沉到壁纸后面） | ❌ 不可用 |

**P0 判通过**，进入 P1（WinUI 壳）。宿主策略定为 **owner + WS_EX_TOOLWINDOW**。

### 10.6 P1 结果（2026-10-21 22:5x）：通过，壳已能跑

启动即崩的三个连环坑，全部定位并修掉：

1. **静默 fail-fast（WER: 0xC000027B / combase.dll），托管日志停在 MainWindow ctor**
   —— csproj 把 PRI 生成关了（`MicrosoftProjectPriGenerationEnable=false` 等），
   MSIX 里没有应用自己的 resources.pri，`MainWindow.xbf` 走 MRT 解析失败。
   修复：恢复 PRI 生成；打包时把 `DeskNest.WinUI.pri` 复制为包根的 `resources.pri`。
2. **MakePri 0x80070005（MSB6006）**，两个独立原因（§10.3 的"中文路径不成立"结论只验证了 `/?`，
   对带真实路径的 `New` 不成立）：
   - 项目路径含中文（`C:/Users/李/`）被 ANSI 的 MakePri 损坏成 `C:/Users/Ng/` → PRI180 不存在。
     修法：建目录联接 `C:/DNB` → 项目根，从联接路径构建。
   - `%TEMP%` 在工作区之外 → exit 5（access denied）且**零报错**。修法：TEMP/TMP 指到
     联接下的 `_tmp`（ASCII 且在项目内）。
3. **补回 PRI 后仍崩，但变成正常托管异常**：`XamlParseException: Cannot find a Resource
   with the Name/Key TabViewButtonBackground` —— 前一晚把 `App.xaml` 的 `XamlControlsResources`
   当"重复"删掉了；它是 NavigationView 等控件主题资源的来源，**不能删**。已恢复。

结果：日志走完全程（App ctor → OnLaunched → MainWindow InitializeComponent → Navigate →
Activate → `nav -> overview`），窗口真实出现（标题「桌巢 DeskNest」，1536x814，可见可响应），
截图 `_re/shots/winui-shell.png`（PrintWindow 抓取，**不进仓库**）。

一键脚本：新增 `tools/pack-shell.ps1`（构建 → ASCII 暂存 → makeappx → `dist\winui`；加 `-Install`
直接注册并启动）与 `build-shell.cmd`（双击入口）；`install-shell.ps1` 补了
`$ProgressPreference="SilentlyContinue"`（否则 Expand-Archive 刷上千行进度）。

P1 完成判据（§5）：NavigationView + 5 页切换 ✔（`nav ->` 日志与页内容渲染均正常）；
**深浅色切换待用户现场看一眼**。下一步 P2（桌面盒窗口）。

## 11. 路线转正（2026-10-22）：WinUI 成为主线，WPF 冻结

用户拍板「重写整个 UI → 直接转 WinUI 3」。docs/29（不换框架重写界面层）随之作废、留档备查。
WPF 版 v2.0 八条反馈已全部清零（③⑧ 已进冒烟，105/105），转入冻结维护。
当前阶段：**P2 桌面盒窗口**（按 §5 判据：盒子贴桌面层、能拖、能读文件列表、能卷起）。

### 11.1 P2 结果（2026-10-22 08:2x）：盒子窗口四件套全通

`src/DeskNest.WinUI/Windows/BoxWindow.xaml(.cs)`，真机打包验证：

- **自绘标题栏**：Win32 摘掉 WS_CAPTION/WS_SYSMENU/最小最大化（`SetWindowLong` + `SWP_FRAMECHANGED`），
  XAML 自绘 36 DIP 标题栏（名称 + 卷起 + 隐藏）。
- **挂桌面层**：`DesktopLayer.AttachOwner`（Native 新增；`SetWindowLongPtr(GWLP_HWNDPARENT, 图标宿主)`
  + WS_EX_TOOLWINDOW，**读回 owner 核实**），日志 `AttachOwner -> True`。
- **手动增量拖动 + 吸附**：PointerPressed/Moved/Released + CapturePointer；全程物理像素
  （`GetCursorPos` 与 `AppWindow.Position` 同为物理像素，天然绕开 WPF 版 DPI 换算坑）；
  吸附复用 Core `WindowSnap`（其它盒子 + 主屏工作区）；落点换回 DIP 存库，与 WPF 版位置互通。
  点在标题栏按钮上不触发拖动（祖先链查 Button）。
- **文件列表**：App 启动时 `Db`+`Repository` 直连原库（WAL 模式，与 WPF 版并存不打架），
  恢复所有"显示中"盒子；`items = 0/…` 日志核对。
- **卷起**：先放开 `OverlappedPresenter.PreferredMinimumHeight` 再设高度（老坑）；
  库里 `RolledUp=true` 开局即卷起——实测 height=45（36 DIP × 1.25）✔。

自证：`_re/shots/p2-box.png`（展开的演示盒）、`_re/shots/p2-box-rolled.png`（卷起的真实盒子，
320x36 DIP 一条标题栏）· 日志全链路 · WPF 侧回归冒烟 105/105。
下一步 P3：盒子菜单/拖文件进盒子/新建删除入口；主窗口 5 页内容填充。

### 11.2 P3 结果（2026-10-22 09:0x）：盒子四交互 + 收纳盒页真实数据

- **拖文件进盒子**：BoxWindow DragOver/Drop，StorageItems 真移动（FileOps.MoveIntoStorage）；
  文本拖入（此电脑/回收站等虚拟图标）走 VirtualShellTargets 建 .lnk；拖入时边框高亮 + 状态条提示。
- **右键菜单**（标题栏与列表区）：打开存储位置 / 添加文件(FileOpenPicker 挂 HWND) /
  重命名(ContentDialog) / 卷起 / 隐藏(写库+关窗) / 删除(确认框→BoxService.Delete 还原文件)。
  双击列表项打开文件。
- **BoxesPage 真实数据**：新建盒子（ContentDialog：名称+普通/映射，只建空盒不动文件）、
  列表行 = 名称/类型/文件数/显示状态 + 显示隐藏开关 + 打开 + 删除；App.OpenBox/CloseBox
  统一管窗口（去重、取消隐藏），App.BoxesChanged 事件让页面跟窗口状态同步。
- **坑（大）**：`MenuFlyoutItem.Icon="Hide"` 不是合法 Symbol → XamlParseException 崩盒子窗口，
  菜单图标全改 FontIcon。
- **坑（巨大）**：`_re/shots/capture_window.py` 是 DPI 无感知的——125% 缩放的窗口被它截成
  1536×814（实 1920×1017），右边 20% 永远缺。曾因此误判「按钮被挤出卡片」折腾三轮，
  其实布局一直是对的（app 日志 TransformToVisual 坐标自证）。已修：脚本开头
  SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)。**以后验证以窗口内日志坐标 + 全屏
  ImageGrab 为准。**
- 自证：p3-fullscreen.png（收纳盒页三行 + 开关/打开/删除全可见）、p3-final-box.png
  （桌面卷起盒子条）。WPF 回归冒烟 105/105。
- 待真机：拖文件进盒（注入无效）、右键菜单点击、深浅色切换。

### 11.3 P4 结果（2026-10-22 09:4x）：五个页面全部有真内容

- **概览**：实时统计（盒子数 / 显示隐藏 / 盒内文件 / 数据库大小）+ 常用操作（去收纳盒页、
  全部显示/隐藏、打开数据目录）+ 最近更新的盒子（直接开窗）。订阅 BoxesChanged 自动刷新。
- **外观与主题**：配色（跟随系统/深色/浅色/纯黑/护眼）+ 圆角滑块，写 `theme.desktop.scheme|corner`
  （与 WPF 版同一批键），改完**即时**应用到所有盒子窗口 + 主窗口；预览卡跟着变。
- **数据与备份**：数据位置五项、搜索索引条目数 + 清空（DELETE+VACUUM）、备份列表 + 立即备份
  （复用 Core BackupService，zip 与桌面版互通）。
- **关于**：版本/构建时间/技术栈/运行时/系统/数据目录/日志、打开日志目录、项目主页、许可证说明。
- 新增 `Pages/PageKit.cs`：卡片/统计块/信息行/操作行/分隔线统一组件。

**本轮的三个坑（都值得记住）**：

1. **`MenuFlyoutItem.Icon="Hide"` 之类非法 Symbol 会炸窗口**（上一轮）；本轮同类问题没再犯，
   但记一笔：WinUI 的枚举字符串解析失败只抛 XamlParseException，不指出哪一行。
2. **代码里设的主题画刷不跟主题变**：`Application.Current.Resources["CardBackground…"]`
   拿到的是固定值；而 `Application.Current.Resources.ThemeDictionaries` 在 WinUI 3 里是**空的**
   （系统主题字典在 XamlControlsResources 内部，不合并到应用级），回退值是 `#B3FFFFFF`
   （70% 白）——切深色后白字压浅底，用户此前反馈"看不清"根因同源。
   修法：App.xaml 里自己定义 `Dn*` 主题字典（Default=深 / Light=浅），PageKit 按元素
   `ActualTheme` 查字典并订阅 `ActualThemeChanged` 重新上色。
3. **外观页的控件回填会污染设置**：`Slider.Value = 12` 触发的 ValueChanged 会把半路值写回设置
   （实测把圆角写成了 2）。修法：`_syncing` 闸门，回填期间忽略变更事件。

**验证**：临时巡页（自动切 5 页）逐页截图 + 日志；设置写 Dark/圆角 20 重启后
`look: theme=Dark, corner=20` 且截图深色可读；恢复 FollowSystem/12 后浅色截图正常；
WPF 冒烟 105/105。巡页临时代码已删。

**顺手发现的真问题**：本机 `desknest.db` 有 **1.7 GB**，其中 `search_index` 表 **334 万行**
（全盘搜索把整块磁盘都索引了）。数据页已加"已索引条目 + 清空索引"入口；
是否清空由用户决定（索引可再生，但不是我该替他删的东西）。

### 11.4 P5 结果（2026-10-22 10:0x）：桌面组件窗口 + 四种组件

`src/DeskNest.WinUI/Windows/WidgetWindow.xaml(.cs)`，骨架与盒子窗口同一套（摘系统标题栏、
挂桌面层 owner、手动拖动 + 吸附、收起、位置存库 DIP 与 WPF 互通）。内容按 `WidgetKind` 分支：

- **Glance**：日期 / 秒级时钟 / 待办与盒子统计 + 跳页按钮
- **Todo**：内联输入回车新增、勾选完成写库、优先级加粗、已完成计数
- **QuickNote**：标题新增 + 最近 20 条便签卡片（标题 + 三行摘要）
- **SystemMonitor**：PerfSampler 1.5s 采样，CPU% / 内存（已用/总量）/ 上下行速率

未移植：File / Weather / Music（File 功能与盒子重复；Weather/Music 依赖外部服务，另行决定）。
概览页新增「桌面组件」卡：四个「＋」新建按钮（错位摆放）、组件列表的打开/删除。
真机自验：四种组件各开一个（内存记录，不写库），owner=True、内容构建成功、截图四张。

### 11.5 收纳盒界面改版（2026-10-22 10:1x）：真图标网格

用户反馈「收纳盒的 UI 还是不太好，自己找素材借鉴一下」。借的东西写在下面，都是能落地的具体做法：

| 参考 | 借了什么 |
|---|---|
| Fences（Stardock 桌面围栏） | 无边框半透明容器、标题栏左侧小图标 + 名称、右上角数量徽章、操作按钮平时隐藏 |
| Windows 11 资源管理器 | 图标在上/名称两行居中的网格、双击打开、单击选中、悬停高亮 |
| Fluent 2 / DeskBox 间距规范 | 8 的倍数间距、容器圆角 14、卡片圆角 8、11.5px 辅助文字 |

改动：
- **真文件图标**：`ShellIcons.ExtractPixels`（HICON → DrawIconEx 画进 32bpp DIB → BGRA 像素），
  WinUI 侧 `IconLoader` 用 `WriteableBitmap.PixelBuffer` 直填 + 缓存（600 条上限）。
  WinUI 没有 WPF 的 `CreateBitmapSourceFromHIcon`，所以走"像素直填"这条唯一可行的路。
- **图标网格**：`GridView` + `ItemsWrapGrid`（84×86 单元，图标 40、名称 11.5 两行定高），
  行高固定后两行文字不再把下一排顶歪。
- **标题栏**：38 高、左侧文件夹字形 + 13px 半粗名称 + 数量胶囊；卷起/隐藏按钮默认透明，
  鼠标移入才显形；双击标题栏 = 卷起/展开。
- **空态**：圆角虚框 + 图标 + 「把文件拖进来 / 或右键添加文件」。
- **单项右键**：打开 / 在资源管理器中显示 / 移出盒子（还原到原位置后删记录）。
- **收纳盒页**：每行左侧加"盒内前 3 个文件的真图标"小预览（空盒显示带底色的文件夹方块）。

自证：`_re/shots/box-ui-v4.png`（6 个样本文件：exe / 文件夹 / 图片 / 文本图标全部正确）、
`box-empty-v2.png`（真实空盒的空态）、`boxes-page-v4.png`。

### 11.6 P5 补齐（2026-10-22 11:4x）：文件 / 天气 / 音乐三类组件

- **文件组件**：绑定一个收纳盒（未绑定时列出盒子让你点选，写回 `Widget.BoxId`），
  列表显示真图标 + 名称，双击打开，顶部「刷新」。绑定信息存库，重启仍在。
- **天气组件**：Open-Meteo（免费、无需 Key）。城市名→geocoding→当前温度/风速 + 未来 3 天最低最高。
  结果写 `weather.summary` / `weather.updated`（与 WPF 版同一批键），「设置城市」用 ContentDialog。
  真机验证：深圳 29.3°C、风速 9.7 km/h、三天预报，日志 `天气已更新（深圳 29.3°C）`。
- **音乐组件**：媒体键（`MediaKeys.Send`，上一首/播放暂停/下一首），不需要权限；
  系统媒体会话（SMIC/SMTC）读取封面与进度留作后续。

**这轮修的两个坑**：
1. **组件内容重建会叠加**：只有 `BuildContent()` 开头清空，`BuildWeather()` 这类"局部重建"
   再往 Content 里 add 就叠一份——天气组件出现两个「天气」块。修法：每个 `Build*` 开头清空。
2. **标题栏按钮字形不对**：`Content="—"` / `"✕"` 在 WinUI 下渲染成空白/怪符号，
   统一换成 `FontIcon`（E70D 卷起 / E70E 展开 / E711 关闭）。顺带纠正方向：
   E70E 是"向上"不是"向下"，之前卷起/展开的箭头是反的。

组件面板现在 7 类全齐：概览、待办、便签、文件、天气、音乐、性能。

### 11.7 组件右键菜单（2026-10-22 21:1x）

`WidgetWindow` 根容器挂 `MenuFlyout`，四项（含两条分隔线）：

- **绑定盒子…**：`ListView` 列出所有盒子（名称 + 项数），当前绑定项预选中；确认后写
  `Widget.BoxId` 与名称。**只有文件组件显示这一项**，其它类型 `Visibility=Collapsed`
  （不给不存在的功能留入口）。
- **重置位置**：主屏工作区水平居中、靠上 80 DIP，写库。
- **重命名…**：改组件标题与窗口标题，写库。
- **关闭组件**：`IsVisible=false` 后关窗（与标题栏 ✕ 同一路径）。

自验：文件组件菜单探测 `6 项，绑定=Visible`，待办组件 `6 项，绑定=Collapsed`；
重置位置实测 `(1125,650) → (1105,100)`。

**新坑（值得记）**：重置位置会 `Upsert` 组件记录——用"内存里的探针组件"做自验时，
一调这个方法就把探针**写进了真实数据库**，之后每次启动都冒出一个「自验-菜单」组件。
自验记录必须用 `name like '自验%'` 清理，或探针一律走不落库的路径。

### 11.8 组件外观：材质与不透明度（2026-10-22 21:3x）——含一个硬限制

**做了什么**：`Windows/WindowLook.cs` 统一给盒子与组件套外观；组件右键「外观…」可单独设
材质（亚克力/云母/纯色）与不透明度（0.35–1.0），保存为"单独样式"（`StyleScope=Override`）；
外观页有全局「材质与不透明度」（写 `theme.desktop.material/opacity`，与桌面版同键）。

**硬限制（Win10 上做不到，必须记住）**：

| 方案 | 结果 |
|---|---|
| `Window.SystemBackdrop = MicaBackdrop / DesktopAcrylicBackdrop` | Win10 build 19045：**不报错、无效果**（SystemBackdrop 实际是 Win11 才生效） |
| `SetWindowCompositionAttribute`（Win10 经典磨砂） | 返回成功，但对 WinUI 3 窗口（`WS_EX_NOREDIRECTIONBITMAP`）**画面无变化** |
| `WS_EX_LAYERED + SetLayeredWindowAttributes`（整窗 alpha） | **直接返回 false**：没有重定向位图可做 alpha |

老 WPF 版是靠 `AllowsTransparency + Window.Opacity` 实现的，WinUI 3 **没有等价能力**——
这是迁移的真实代价，不是配置问题。代码现在按平台能力降级：`IsTransparencySupported`
（build ≥ 22000）为 false 时强制纯色、界面上的材质/不透明度控件禁用并写明原因
（"升级到 Windows 11 后自动生效"），设置值照常保存。

**验证**：Win10 上日志 `系统不支持窗口透明（build 19045），材质降级为纯色`、
`外观：请求=Mica 实际=Solid 底色alpha=255`；外观页截图确认控件禁用 + 说明可见；WPF 冒烟 105/105。

**留给用户的选项**：Win11 上会自动变磨砂；若要在 Win10 上也要"半透明感"，
替代品只能是"底色明暗/浓度"这类假透明——需要用户点头再做。

### 11.9 模拟磨砂（Win10 也能毛玻璃）+ 收起标题被裁修复（2026-10-22 21:5x）

**1) 收起时标题显示不全（用户反馈）**
`SetRolledUp/SetCollapsed` 里窗口高度写死 `36/34 × DPI`，而标题栏在 XAML 里改成了 38，
于是卷起后比标题栏矮 2–3px，最后一行字被裁。改为读标题栏实测高度：
`TitleBarDip = TitleBarGrid.ActualHeight > 0 ? ... : 38（兜底）`，再 `Ceiling(dip × scale) + 1`。
实测收起高 45px，标题完整（截图 `collapsed-title.png`）。

**2) 模拟磨砂（`Windows/WallpaperGlass.cs`）**
真材质在 Win10 上无解（§11.8），但盒子/组件本来就贴在桌面层、背后就是壁纸，所以：
读壁纸（`SPI_GETDESKWALLPAPER`，取不到退 `TranscodedWallpaper`）→ 按"填充"方式换算窗口
在壁纸上的裁剪区 → 双遍盒式模糊（半径 ≈ 宽度/24）→ 叠主题色 tint → 铺成根元素底图。
- 材质新增 `Glass`（磨砂·模拟），任何系统都能用；
- **Win10 上选亚克力/云母会自动映射到 Glass**（日志：`亚克力/云母 -> 模拟磨砂`），
  不再出现"选了材质没反应"；
- 内容区再铺一层半透明面 `DnGlassSurface`（浅色 #8CFFFFFF / 深色 #8C1F1F1F），
  保证文字在花壁纸上仍可读；
- 局限：只模糊壁纸，不含压在壁纸上的其它窗口；换壁纸/换显示器后需重算（下次应用外观时重建）。

自证：`box-glass-final2.png`（盒子背景就是模糊后的壁纸）、`glass-test.png`（两个组件各自
取到自己位置的那块壁纸）。

**3) 踩到的流程坑**：先把 `material == Glass` 判断放在"降级映射"之前，导致
`请求 Mica → effective=Glass` 之后没走磨砂分支，窗口只剩纯色。正确顺序是
**先算 effective，再按 effective 分流**。

### 11.10 磨砂设为默认 + 视觉收敛（2026-10-22 22:2x）

用户反馈"收纳窗口有点花里胡哨"，两条改动：

**1) 默认材质 = 磨砂（Glass）**
`App.CurrentMaterial` 缺省值从 Acrylic 改为 Glass；Win10 上亚克力/云母本来就会映射到 Glass，
现在连"没设置过"的新用户也直接得到磨砂，不会先看到一块死白。

**2) 视觉收敛（花在哪，就去掉哪）**
- 空态：原来"窗口圆角 + 内层圆角边框 + 图标 + 两行字"是**框套框**，最显花 → 去掉内层边框，
  只留淡图标 + 一行「把文件拖进来」。
- 标题栏：数量从"胶囊底 + 数字"改为普通小字（opacity 0.45）；文件夹图标 12→11 且透明度降到 0.6。
- 磨砂面 `DnGlassSurface`：0.55 白 → **0.90**（浅色 #E6F7F7F9 / 深色 #D91E1E1E）。
  内容区基本是干净的浅面，玻璃只在标题栏与边缘露出——比"整窗透壁纸"稳得多。
- 磨砂图**去饱和 62%** 再叠主题色：真毛玻璃背后是灰的，不去饱和的话花壁纸透上来会艳得刺眼。
  tint 也从 0.10+0.45v 提到 0.22+0.38v（更闷一点）。

自证：`box-quiet3.png`（标题栏 mauve 灰的磨砂条 + 干净内容面），对比 `box-glass-final2.png`
（之前那版艳紫）。WPF 冒烟 105/105。

### 11.11 标题"花"与收起裁切的真因（2026-10-22 22:3x）

**1) 标题栏那条彩色带**
上一版把洗色做成"内容区一层 + 标题栏一层"，结果标题栏透出的是**原始磨砂壁纸**（饱和的紫），
和干净的内容区形成一条明显的彩色带。两次尝试后定了正确做法：
**洗色直接融进壁纸磨砂图**（tintAlpha 0.78–0.92，只留一成多纹理），标题栏与内容都用同一张底图，
两者之间只用一条 1px 细线分隔。少一层 overlay，也就没有"圆角洗色层盖不住 Root 圆角"的 1px 色边。

**2) 收起后标题仍被裁（用户反馈第二次）**
上次改成读 `TitleBarGrid.ActualHeight` 只解决了一半。真因是：
`AppWindow.Size` 是**窗口**尺寸，Win10 的窗口即使摘了标题栏，仍带一圈不可见的拉伸边框
（WS_THICKFRAME），**客户区比窗口小几像素**。按"标题栏高度"设窗口高度，客户区自然不够，
最后一行字被裁。修法：`WindowMetrics.FrameInsets(hwnd)`（GetWindowRect − GetClientRect）
拿到非客户区宽高，收起高度 = `ceil((标题栏 DIP + 2) × DPI) + frame.dy`。
实测收起高 67px，标题完整（截图 `rolled-title.png`）。

**3) 顺带**：自验探针又往库里写了几条「演示收纳盒/外观-*/收起验证」，已用名字清理；
自验结束务必检查 `boxes` / `widgets` 表。

### 11.12 P6：搜索页（2026-10-23 10:1x）

`Pages/SearchPage.xaml(.cs)`，导航新增「搜索」。设计上刻意**不**复用桌面版的 `SearchService`：

| | 桌面版（WPF） | 新版（WinUI） |
|---|---|---|
| 索引加载 | `LoadFromStore()` 把 300 多万条灌进 `ConcurrentDictionary` | **一条都不加载**，搜的时候直接 SQL |
| 查询 | 内存里遍历全部条目 | `WHERE path LIKE '%q%' LIMIT 400`（`Repository.SearchPaths`） |
| 代价 | 常驻内存几百 MB～GB | 每次全表扫描约 1 秒（后台线程 + 280ms 防抖 + 取消上一次） |

理由：本机索引 3,343,351 条，读进字典对桌面常驻进程太贵；搜索是低频操作，一次全表 LIKE 换
几百 MB 常驻内存很划算。将来要提速就上 FTS5（`search_index` 加虚拟表），不是回退到内存字典。

功能：
- 一处输入，两路来源：**本地表**（盒子 / 组件 / 待办 / 便签，毫秒级）+ **全盘文件索引**（SQL）；
- 结果分两张卡：「你的东西」优先，其后「文件（N 条，来自全盘索引）」；
- 文件行：真图标 + 文件名 + 路径 + 大小，双击打开、右键「在资源管理器中显示」；
- 结果排序：文件名开头命中(300) > 文件名包含(200) > 仅路径命中(60)，同分按路径长度；
- 副标题实时显示索引总量，索引为空时给出明确指引。

自验：日志 `SearchPage: 索引 3343351 条`；输入 `win` 后 60 条命中（Windows.Networking.dll、
winutil64.exe、WindowsSpeech_Dictation.dll …），图标/路径/大小都正常（`search-now.png`）。

**踩到的坑**：`DispatcherQueue.CreateTimer()` 出来的定时器如果只被局部变量引用，会被 GC 回收，
Tick 永远不来——自验脚本里连续两次踩到，最终改成 `DispatcherQueue.TryEnqueue` 直接切页。

### 11.13 P4 补完：系统托盘 + 关窗隐藏（2026-11-16 19:1x）

WinUI 3 版此前没有托盘，`MainWindow` 关闭按钮 = 真退出，误关后只能从任务栏/开始菜单重新找——
本轮把原迁移计划里一直欠着的 P4「托盘 / 外围」这块补上。

**Native 层**（`src/DeskNest.Native/Win32/TrayIcon.cs`，新增）：
- 标准 Win32 `Shell_NotifyIconW`（`NOTIFYICONDATAW` + `NIM_ADD/MODIFY/DELETE`），不用 WinForms
  `NotifyIcon`，WinUI 项目不必引 WinForms。
- 消息窗口跑在**独立 STA 线程**（`HWND_MESSAGE` 消息-only 窗口）：右键菜单弹在该线程不卡 UI；
  左键单击 / 双击、菜单点击通过回调交给宿主，宿主用 `DispatcherQueue.TryEnqueue` 切回 UI 线程。
- 右键菜单用 `CreatePopupMenu`/`TrackPopupMenuEx`（`TPM_RETURNCMD`），点击外部自动收起靠
  `SetForegroundWindow` + 投递 `WM_NULL` 的经典顺序。
- WndProc 委托**静态强引用**防 GC（同 `DispatcherQueueTimer` 那个坑）。

**WinUI 侧**：
- `App.xaml.cs`：`OnLaunched` 里 `InitTray()`；菜单 4 项——打开主窗口 / 搜索 / 显示隐藏桌面盒与组件 /
  退出。显示切换只改窗口 `AppWindow.Hide()/Show()`，**不动库里的 IsHidden/IsVisible**。
- `MainWindow.xaml.cs`：`AppWindow.Closing` 拦截，普通关闭 `e.Cancel=true` + `AppWindow.Hide()`（隐藏到托盘）；
  只有走托盘「退出桌巢」时 `App.IsExiting=true` 才放行。
- 图标：`src/DeskNest.WinUI/Assets/desknest.ico`（复用 WPF 版），csproj 用 `Link="desknest.ico"`
  复制到输出**根目录**——不能进 `Assets\` 子目录，否则 pack-shell.ps1 复制 `_msix\Assets` 会叠成 `Assets\Assets`。

**踩到的坑**：WinUI 3 的 `Window` 没有 `Show()/Hide()`/`WindowId.CreateFromHWND`，也没有
`AppWindow.Activate()`——显示/隐藏用 `AppWindow.Show()/Hide()`，激活用 `Window.Activate()`，
`Application.Exit()` 在静态上下文要写 `Current.Exit()`。

**自验**：独立探针（临时目录，不进仓库）实例化 `TrayIcon` —— 日志出现
`托盘图标已创建`（`NIM_ADD` 返回 true，图标真进系统托盘）+ `托盘消息线程退出(GetMessage=0)`
（Dispose 干净收尾，无卡死无崩溃）。编译 0 错误；MSIX 打包 87.3 MB 成功，解包确认根目录与
`Assets\` 下均有 desknest.ico。**右键菜单的点击命中与"关窗隐藏"的真机手感仍需用户亲手试**
（本机鼠标注入无效，见 CONTEXT.md 约定）。

### 11.14 一轮全面测试 + 问题修复（2026-11-16 19:4x）

用户要求"测试一下看有哪些问题改一下"。把项目现有的测试手段全跑了一遍：

| 手段 | 结果 |
| --- | --- |
| 冒烟验证器 `tools/DeskNest.Smoke` | 105/105 全过 → 加断言后 **108/108** |
| `dotnet test`（17 个测试类） | testhost `Access Denied`（已知坑 3，环境问题非代码） |
| 编译（Native / Core / WPF / WinUI 全线） | 0 错误 |
| MSIX 冷启动真机（解包→注册→启动） | 启动链路干净：`主窗口关闭已拦截`、`托盘已初始化`、盒子/组件恢复、壁纸载入，无任何 FAILED/Exception |
| 关窗隐藏真机（对主窗口发 WM_CLOSE） | `visible: True→False`、日志 `主窗口关闭按钮 → 隐藏到托盘`、进程存活 —— **关窗隐藏真机验证通过** |

**修掉的问题**：
1. **文件还原健壮性（真 bug，Core 层）**：`FileOps.RestoreToOriginal(stored, originalPath)` 在
   `originalPath` 为 null（老数据 / 记录不全）时，`Path.GetFileName(null) → Path.Combine` 抛
   `ArgumentNullException`，本可还原的文件变成"移出失败"。参数改 `string?`，空值回退用存储路径的
   目录与文件名还原到原处；WinUI `BoxWindow` 的 CS8604 警告随之消失。冒烟新增 3 条断言固化。
2. **合并盒/组件的可空传递（3 处 CS8604）**：`group.ActiveMemberId` 声明 `string?`，虽被
   `MergeInto`/`MergeWidgets` 逻辑保证非空，直接传给非空形参仍是隐患 → 加 `?? keep.Id` 兜底。
3. **编译噪音**：自动生成文件 `FluentIconData.cs` 补 `#nullable enable`（消 CS8669）；
   `DesignTokens.RowPadding` 去掉多余 `new`（消 CS0109）；冒烟 `GroupSelector` 测试 switch 补
   `_ => null`（消 CS8509）。WPF 侧警告 7 → 1。

**有意保留、不修的**：
- `WidgetWindow.Content` 遮蔽 `Window.Content`（CS0108）：XAML `x:Name="Content"` 生成字段，
  50 处 `Content.Children` 引用，功能正常；改名高回归风险、低收益。
- `app.manifest` 高 DPI 警告 WFO0003：manifest 配的是 PerMonitorV2（比 .NET 属性更精确），
  且 WPF 是冻结维护线，改了反而可能降级。

**发现待用户决定的数据问题（未擅自删除）**：用户库 `desknest.db`（1.73 GB，主要是 334 万条
搜索索引，正常）里有 5 个 hidden 测试盒（"演示收纳盒"/"新收纳盒"，10-17~10-23 开发期创建、
items=0）+ 2 个不可见测试组件，是历史自验残留，污染概览统计（显示"5 个收纳盒"但全隐藏）。
按"个人文件不擅自删"的约定，仅报告，待用户确认后清理。

### 11.15 待办到点提醒（2026-11-16 19:5x）—— HANDOFF 第 3 条核心缺口

补上挂了很久的"待办到点提醒没接"：`PetEngine.RemindTodo` 此前只有演示调用，没有定时器，
`Repeat` 也不生成下一条。按"纯逻辑抽 Core + 界面只调度呈现"的原则做成完整闭环：

**Core 层**（`src/DeskNest.Core/Services/TodoReminderEngine.cs`，新增、纯逻辑）：
- `DueNow(todos, now)`：`DueAt - RemindMinutesBefore <= now <= DueAt + 1min`、未完成/未归档/
  有截止/设了提醒时才返回；`(id,dueAt)` 在 1 分钟窗口内去重，防每轮扫描重复弹。
- `NextOccurrence(todo, now)`：完成重复任务后生成下一轮，截止从 DueAt 推进到 `> now` 的最近一轮
  （Daily/Weekly/Monthly；Lunar 沿用项目既有约定用 29 天近似），新 id、继承标题/提醒/颜色/步骤。
- `Prune(now)`：清理两天前的已提醒记录，防字典无限增长。

**Native 托盘**：`TrayIcon.ShowBalloon(title, text)`——气泡文本走 `ConcurrentQueue` + `PostMessage`
自定义消息，由消息线程投递 `NIM_MODIFY + NIF_INFO`（跨线程不直接碰归消息线程所有的 NOTIFYICONDATA）。

**WinUI**：`App.InitReminders()` 用 `DispatcherQueueTimer`（静态强引用防 GC）每 60 秒扫一次，
启动即扫一次；到点经托盘气泡提示 + 日志。待办组件补全交互：显示截止/重复标记，右键标题弹菜单设
截止（今天18点/明天9点/下周一9点/清除）、提前量（不提醒→准点→5分→30分→1时→1天）、重复周期；
勾选完成时自动生成下一轮（并按 title+dueAt 去重）。

**自验**：冒烟 108 → **117/117**（新增 9 条：到点判断/去重/窗口/Daily-Weekly-Monthly 推进/继承字段）；
全线编译 0 错误；MSIX 真机冷启动日志 `待办提醒已启动（每 60 秒扫描）`；插一条到点待办重启后，
日志出现 `待办提醒：自验-到点提醒2（截止 11月16日 19:56）`——**到点检出 + 气泡投递链路真机通过**。
自验待办已删除、测试进程已结束。

**构建环境插曲**：本会话 PowerShell 工具无法把原生 exe 当命令执行（makeappx 被当"文档"），
且 `LOCALAPPDATA`/`PUBLIC` 环境变量为空导致 NuGet restore `path1` 报错。解法：编译用
`--no-restore`，makeappx 走 Bash + `MSYS2_ARG_CONV_EXCL="*"`（否则 `/d` 被 MSYS 转成 `D:/`）。
打包成功 91.5 MB。这些是环境问题，非代码缺陷。
