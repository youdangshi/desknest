# 第三方组件与素材声明

桌巢 DeskNest 本体以 **GNU GPL v3.0** 开源（见 [LICENSE](LICENSE)）。以下是使用到的第三方组件与素材，各自的许可与来源如下。

## 参考实现（GPL-3.0）

| 项目 | 许可 | 用在哪 | 说明 |
| --- | --- | --- | --- |
| [Tianyu199509/DeskBox](https://github.com/Tianyu199509/DeskBox) | **GPL-3.0** | 收纳格/胶囊收起/组合排列/设置页信息架构 | 2026-10-17 起本项目同为 GPL-3.0，允许复用其代码与 XAML；复用的文件在文件头注明「源自 DeskBox（GPL-3.0）＋修改说明」，并保留原作者版权声明 |

### 为什么会出现在这里

DeskNest 最初是 MIT。收纳盒的分组标签、胶囊收起、组合排列这些交互，DeskBox 已经做到位，而它是 GPL-3.0——
把 GPL 代码混进 MIT 项目会让整个项目被迫变成 GPL，属于许可混用。项目负责人 2026-10-17 选择**整体切到 GPL-3.0**，
换取"可以直接复用实现"，因此在此登记来源与义务。
## 运行时依赖

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) / SQLitePCLRaw | 本地数据库 | MIT |
| [SQLite](https://sqlite.org/) | 数据库内核 | Public Domain |
| .NET 10 / WPF / Windows App SDK 相关运行库 | 运行框架 | MIT |

## 图标与图形素材

| 素材 | 来源 | 许可 | 说明 |
| --- | --- | --- | --- |
| **Fluent 图标（20px regular，共 55 个）** | [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) | **MIT** | 由 `src/DeskNest.App/Ui/FluentIconData.cs` 顶部的生成说明可追溯：脚本从上游仓库下载 SVG，仅提取矢量路径数据后嵌入代码；未使用任何第三方应用的素材文件 |
| 天气图标（预留，暂未内置） | [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) / [Meteocons](https://github.com/basmilius/weather-icons) | MIT | 需要时按下表许可引入 |
| 桌宠形象与插画 | 本项目自绘 / 用户用本机 ComfyUI 生成 | 由归属者决定 | 详见 `docs/05-美术资源方案.md` |

## 字体

| 字体 | 来源 | 说明 |
| --- | --- | --- |
| Segoe UI Variable / Segoe UI / Segoe Fluent Icons / Segoe MDL2 Assets | Windows 系统自带 | 不随本程序分发，仅按系统字体引用 |
| Microsoft YaHei UI | Windows 系统自带 | 同上 |

## 关于竞品研究

本项目在立项阶段对若干同类软件做过**只读的逆向分析**（DeskBox、小智桌面、360 桌面精简版等），用于对比功能与界面设计。

- **DeskBox**（`Tianyu199509/DeskBox`）为 **GPL-3.0** 开源项目。我们**只参考它的设计度量与交互思路**（例如卡片圆角、行高、设置页分组方式），**没有复制任何代码、XAML、SVG 或其它素材文件**。
- 小智桌面与 360 桌面精简版为闭源商业软件，仅从安装包的结构、目录、公开配置中提取功能对照信息，未使用其任何文件。
- WitchDrawer（PolyForm Noncommercial）与 ZenDesktop（GPL-3.0）同样只作思路参考。

如果将来需要引入上述项目的**具体文件**，必须先确认许可兼容性（本项目为 MIT，不能引入 GPL/AGPL 代码）。
