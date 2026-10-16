# 桌巢 DeskNest · 交给 dsh 的接手须知

> 写于 2026-10-16 21:26（HEAD `8cd7521`，已推 GitHub `youdangshi/desknest` main）。
> 这份文件是**给下一个 agent（dsh / VS Code 里的 Codex / Claude Code）的入口**。
> 项目根另有 `HANDOFF.md`（交接快照；**原「每天 9:30 / 21:30 自动覆盖」的钩子已于 2026-10-16 停用，现在靠手工更新**）和 `CONTEXT.md`（跨工具共享的进行中工作与已定决策，追加式）。

## 0. 一句话

Windows 桌面整理软件（收纳盒 + 桌面组件 + 全盘搜索 + 桌宠 + 可选模块），C# / WPF / .NET 10，MIT 开源。
**功能面已经齐了，现在的瓶颈是"界面的手感与可读性"**——用户反馈集中在两件事上（见第 2 节），这两件事没解决前别再往上堆新功能。

## 1. 接手第一步（建议按顺序做，半小时内能到"能改能动"）

```powershell
cd "C:\Users\李\Desktop\项目组\desktop-organizer"
git log --oneline -8            # 看最近改动
git status --short              # 应该干净；有改动先看 HANDOFF.md 的"最近改动"里有没有说明
```

1. 读 `CONTEXT.md`（已定决策、约定、红线）→ 读 `HANDOFF.md`（现状与下一步）→ 读 `docs/10-桌巢-完整软件策划书.md`（完整策划书 + 验收记录）。
2. 编译 + 跑冒烟（命令见第 4 节），确认基线是"0 错误 / 40 项全过"。**不要**先急着改代码。
3. 挑第 2 节里的第 1 条开工（那是用户最急的）。
4. 改完：编译 → 冒烟 → 更新 `docs/10` 的验收记录与 13.1/13.2 → 提交 → 推送（命令见第 4 节）。

## 2. 现在最该修的两件事（用户已连续反馈）

### 2.1 改「外观与主题」的参数，盒子的颜色根本不变 ⚠ 优先

**现象**（用户原话）：「改窗口的外观参数盒子的颜色根本不变」「有些按钮调节没有反应」。

**根因**（已读代码确认，不是猜）：

- 盒子与组件的底色分别由 `MakeBackground()`（`src/DeskNest.App/Windows/BoxWindow.cs` 196–203 行）
  和 `WidgetWindow.ApplyLook()`（`src/DeskNest.App/Windows/WidgetWindow.cs` 160–172 行）画出；
- 这两个函数**只用了 `t.Background` 和 `t.Opacity`**，拿到手的 `Material` / `MaterialIntensity` **一次都没读** → 外观页那两个控件对盒子是**死控件**；
- `t.Background` 只随「配色方案（深色 / 浅色 / 纯黑 / 护眼）」变，**产品里没有"卡片颜色"这个入口**，所以用户想"给盒子换个颜色"根本找不到地方。

**建议改法**（可以拆成三步走，每步都能单独验）：

1. `ThemeTokens` 加一个 `CardColor?` 覆盖项，外观页加「卡片颜色」取色器 + 几个预设色；`MakeBackground` 优先用它。
2. 让「材质强度」真正参与绘制（纯色 = 现在的行为；亚克力/云母 = 按强度调整底色不透明度或接入窗口 backdrop），**并让"材质"这个下拉真的改变绘制路径**，否则还是死控件。
3. 盒子菜单「更多操作」里补「材质 / 材质强度 / 颜色」（现在只有 透明度 / 圆角 / 样式作用域）——用户的诉求是"能统一所有盒子，也能单独控制某一个"。
4. 冒烟验证器（`tools/DeskNest.Smoke/Program.cs`）加断言：改颜色/材质后 `MakeBackground` 出来的 RGB（或 alpha）**必须真的变**。

### 2.2 用户说"根本看不清选项是什么"（菜单 / 盒子上的文字）

**已做的**（本轮 `8cd7521`）：菜单文字色改为「按菜单自己的底色算对比度」、菜单字号 13 → 14、行高 30、内边距放宽。

**证据**（别再用肉眼争）：对用户截图做像素采样，菜单底色 `#28282E`、文字 `#F5F5F5`，对比度 **12:1**（WCAG AAA 是 7:1）——
也就是说**对比度本来是达标的**，用户感受到的"看不清"主要来自 13px 中文小字在 1080p 上笔画挤成一团。

**下一步建议**：

1. 先请用户**截图圈一下**到底哪一处看不清（菜单？盒子里文件名？标题栏按钮？），一次问清，别猜着改。
2. 做一个全局「界面文字大小」（13 / 14 / 15）+「高对比」开关，一次覆盖菜单、盒子标题、盒内文件名（用户的一贯偏好是"每个模块都能自己决定"）。
3. 判断可读性一律用像素采样算对比度（`System.Drawing` + `LockBits`，别用逐像素 `GetPixel`，会慢到几分钟）。

## 3. 用户的红线（踩了就白干）

1. **不许擅自移动/整理用户的桌面文件**。历史事故：「新建格子」顺手跑了一次桌面分区整理，把用户 20 个文件搬进存储目录还没登记 → 文件"消失"。
   现在 `PagesBoxes.NewBoxDialog` **只创建空盒子**，「整理文件」是独立入口（去「整理与规则 → 一键整理」，预览 + 可回滚）。**别把整理逻辑塞回新建流程。**
2. **绝对不要安装 360 桌面 / 小智桌面 / DeskBox 等竞品**（逆向只做静态分析：看安装包、看资源、看配置，不运行）。
3. **不注册系统右键菜单**、不弹广告 / 推荐、不上传用户数据、基础功能不设付费墙、不做壁纸库。用户明确说"别学小智桌面往桌面右键菜单里塞一堆"。
4. **每个功能都要能单独关掉**：新功能默认关，或至少在设置里给开关（录屏 / 桌宠 / 性能显示 / AI 接入都是这个模式）。
5. **隐私**：`docs/images` 只放 20 张数字开头的界面截图；`_` / `t-` / `visit-` 开头的**全屏测试截图绝不能进仓库**（会拍到用户其他窗口）。
   推送 token（PAT）只走命令行，**不要写进任何进仓库的文件**。
6. 界面文案一律中文；用户是小白，解释要"先结论、再机制"、用大白话。
7. **每次改动都要同步 `docs/10` 策划书**（功能追加到验收表、缺陷写进 13.1、解决掉的从 13.2 删掉），提交信息用 `docs: 策划书同步（要点）`。
   原来有个定时钩子 `doc-sync-10-22`（每天 10:00 / 22:00）兜底做这件事，**2026-10-16 已按用户要求停用**，所以现在完全靠自觉。

## 4. 环境与命令（照抄即可）

```powershell
$env:DOTNET_CLI_HOME="C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES="C:\Users\李\Desktop\项目组\.nuget"
$dn="C:\Users\李\.dotnet-sdk\dotnet.exe"
cd "C:\Users\李\Desktop\项目组\desktop-organizer"

& $dn build DeskNest.slnx                                   # 编译（沙箱内若报文件被锁：停掉 dotnet 进程再试）
& $dn build tools\DeskNest.Smoke\DeskNest.Smoke.csproj -c Release
& $dn exec "tools\DeskNest.Smoke\bin\Release\net10.0-windows\DeskNest.Smoke.dll"   # 40 项断言，当前唯一自动化验收

# 跑起来看界面（必须用签名宿主；直接跑 exe 会弹「无法验证发布者」）
Start-Process -FilePath $dn -ArgumentList @("exec","src\DeskNest.App\bin\Debug\net10.0-windows\DeskNest.dll") -WorkingDirectory (Get-Location)

# 推送（本机直连 GitHub 不通，必须带代理）
git -c http.proxy=http://127.0.0.1:7897 -c https.proxy=http://127.0.0.1:7897 push "https://<PAT>@github.com/youdangshi/desknest.git" main
```

- **`dotnet test` 在本机跑不起来**（testhost `Access Denied`，环境问题），别反复试；测试代码在 `tests/DeskNest.Tests`（91 个测试方法），
  验收一律用 `tools/DeskNest.Smoke`。改了核心逻辑（整理 / 删除 / 合并 / 几何 / 主题）就顺手往冒烟里加一条断言。
- **沙箱外执行**才连得上网 / 才写得了用户目录；`.ps1` 脚本必须纯 ASCII（中文会破坏 `powershell.exe -File` 的解析）。
- **鼠标/键盘注入在这台机器上无效**（`SendInput` 返回成功但应用收不到），所以"拖动能不能拖""按钮点不点得到"这类事只能：
  ① 让用户亲手试；② 用 `_re\shots\probe-*.ps1` 打印窗口层级/样式位找证据；③ 把逻辑抽成纯函数写进冒烟验证器。
- 数据目录 `%LOCALAPPDATA%\DeskNest`（`DESKNEST_DATA_DIR` 可覆盖）；`_re\` 不进仓库。

## 5. 核心机制速览（改代码前先看，省得踩老坑）

- **盒子不是桌面层窗口**：`DesktopLayer.Attach` 现在直接 `return false`（挂 WorkerW 会变成"看不见也点不到"）。盒子是**可激活的普通窗口** +
  点击置顶 + 拖动期间 Topmost；代价是会被别的程序盖住（见 `docs/10` 13.2）。
- **两套外观令牌**：`Theme.Shell`（主窗口/设置页）与 `Theme.Desktop`（盒子/组件/桌宠），互不影响，可互相复制。外观页改哪套要看清 `theme.shell` / `theme.desktop` 前缀。
- **拖动一律走 `WindowDrag` 的手动增量实现**（`WM_NCLBUTTONDOWN` 那条路对盒子静默失效却返回成功，别改回去）；DPI：`PointToScreen` 是物理像素，`Window.Left/Top` 是 DIP。
- **菜单配色要在 `ApplyMenuTheme` / `PaintItems` 改**，改 `Item()` 会被覆盖；子菜单是独立 Popup、不继承配色（详见 `HANDOFF.md` 已知坑 21）。
- **数据库**：SQLite 在 `%LOCALAPPDATA%\DeskNest`，加列要走 `Db.EnsureColumn(..., "DEFAULT ...")`，读可空列用 `GetDateOrNull/GetIntOrNull` 之类（老库里的 NULL 会把整页读崩）。

## 6. 文档索引

| 文件 | 讲什么 |
| --- | --- |
| `docs/10-桌巢-完整软件策划书.md` | **完整策划书**：做什么 / 给谁 / 怎么设计 / 怎么测 / 怎么发 + 核心功能验收记录 20 条 + 缺陷现象·根因·处理（13.1）+ 未解决（13.2） |
| `docs/09-桌巢-完整软件方案书-v2.0.md` | 逐页截图版方案书（20 张真实截图） |
| `docs/08-桌面交互规格与鼠标测试清单.md` | 每一层鼠标动作该有什么反应（交互规格） |
| `docs/ARCHITECTURE.md` / `CLAUDE.md` | 架构说明 / 项目开发说明（单一事实来源） |
| `HANDOFF.md` | 交接快照（现状 / 最近改动 / 下一步 / 未决问题 / 环境 / 已知坑），**手工更新**（原自动覆盖的钩子已停用） |
| `CONTEXT.md` | 跨工具共享的进行中工作与已定决策（**追加式**，别重写别人的内容） |

改完记得**追加一行到 `CONTEXT.md` 的「快照索引」**，让 Claude Code 那边也看得到。
