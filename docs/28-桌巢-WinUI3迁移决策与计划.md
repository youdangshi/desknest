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
