# 桌巢 DeskNest

# 桌巢 DeskNest

<img src="src/DeskNest.App/Assets/desknest-logo-256.png" alt="DeskNest" width="96" />

把乱桌面变成"会自己收拾、能常驻有用信息、还有点好看"的桌面。

Windows 10/11 桌面管家：**桌面整理 + 桌面组件 + 工作伙伴桌宠 + 本地 AI 出图**，四个能力在一个程序里。
MIT 开源，数据全部留在本机。

---

## 它有什么

| 能力 | 说明 |
| --- | --- |
| **桌面收纳盒** | 四种盒子：普通盒（文件移动进来）、映射盒（只存路径不动文件）、抽屉盒、待办盒；可拖拽缩放、卷起、自动隐藏，一个盒子一个样式 |
| **盒组** | 多个盒子叠在同一个位置，标题栏点击/滚轮切换（像浏览器标签页），桌面空间立刻省一半 |
| **全盘搜索** | `Alt+Space` 呼出 Spotlight 面板：文件、应用、命令都能搜；索引持久化，重启不用重扫 |
| **一键整理** | 扫描 → 预览 → 执行 → 不满意就撤销；规则整理、自动整理（默认关） |
| **两种整理形态** | ①**文件夹归集**：在桌面按类型建文件夹；②**桌面分区**：文件收进桌面上的盒子，桌面不留文件夹 |
| **桌面组件** | 待办、随记、天气、音乐、系统监视、概览；可编组、可收成胶囊条 |
| **桌宠（助手型）** | 待办提醒、番茄钟、久坐提醒、系统状态联动、AI 问答；打字陪伴默认关闭且只统计"有没有按键" |
| **本地 AI 出图** | 连接你自己电脑上的 **ComfyUI**（默认 `127.0.0.1:8188`）出图，用你的模型，图片存本地 |
| **应用使用统计** | 类似 ManicTime：每个应用用了多久，默认只记进程名 |
| **性能监视** | CPU / 内存 / 网络 / GPU，显示项与布局自己选（游戏加加那种） |
| **API 面板** | 各模型余额、调用次数、成功率、平均延迟；Key 用 Windows DPAPI 加密 |
| **计划关机** | 定时 / 倒计时 / 空闲触发，提前提醒、可顺延、可取消 |
| **AI 接口** | 只读本地接口，把盒内文件元数据、待办、随记、统计开放给本机 Codex / dsh / Claude |

### 永久不做

广告、推广位、桌面/资源管理器右键菜单注入、注入其它进程、静默上传数据、基础功能付费墙。

---

## 安装与运行

1. 下载 Release 里的 `DeskNest-<版本>-win-x64.zip`，解压到任意目录（**自包含**，不需要装 .NET）。
2. 双击 `DeskNest.exe`。
3. 首次运行会打开主窗口，从「概览」开始。

快捷键：

| 快捷键 | 作用 |
| --- | --- |
| `Alt + Space` | 呼出/隐藏快捷搜索 |
| `Alt + 反引号` | 显示/隐藏全部桌面盒与组件 |
| `Ctrl + 滚轮` | 在盒子上调图标大小 |
| 双击桌面空白 | 切换桌面图标显示（可关） |

### 用 ComfyUI 出图

1. 用「绘世启动器」等正常方式启动 ComfyUI（默认 `http://127.0.0.1:8188`）。
2. 桌巢 → 左侧「AI 出图（ComfyUI）」→ 点「测试连接」读取模型。
3. 选模型、写提示词、设参数 → 「开始出图」。图片默认存到 `图片\DeskNest 出图`。

出图后可以右键图片：打开、复制路径、设为桌宠形象、删除。

---

## 开发

需要 **.NET 10 SDK**（`net10.0-windows`）。

```
dotnet build DeskNest.slnx
dotnet test  tests/DeskNest.Tests/DeskNest.Tests.csproj
```

发布自包含版本：

```
dotnet publish src/DeskNest.App/DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist/DeskNest-1.0.0
```

数据目录：`%LOCALAPPDATA%\DeskNest`（可用环境变量 `DESKNEST_DATA_DIR` 改到别处，便携模式就是把它指到程序目录）。

### 工程结构

```
src/DeskNest.Core     逻辑与数据：模型、SQLite、文件安全操作、整理引擎、搜索、备份、ComfyUI 客户端……
src/DeskNest.Native   和 Windows 打交道：桌面层、拖放、Shell 图标、全局热键、系统指标、关机、DPAPI
src/DeskNest.App      界面：主窗口（行式设置页）、桌面盒、组件、桌宠、搜索面板、托盘
src/DeskNest.Module.* 可选模块（独立 dll，放到 modules\ 才会加载）
tests/DeskNest.Tests  单元与集成测试
docs/                 策划书、架构、冒烟清单
```

模块机制：`modules\` 目录下的 `DeskNest.Module.*.dll` 会被扫描加载；**没放进去的模块等于没安装**，不加载、不占内存、界面里也不出现。

---

## 文档

- **[完整软件方案书 v1.1](docs/06-桌巢-完整软件方案书-v1.1.md)**：定位 / 竞品与美术资源研究 / 功能清单 / 架构 / UI 设计规范 / 测试 / 路线图（对外交付用这一份）
- [策划书 v0.1](docs/01-策划书-v0.1.md)：竞品逆向、功能清单、数据模型、逐屏 UI 设计
- [策划书 v0.2](docs/02-策划书-v0.2.md)：你的全部反馈落地、大白话技术路线、安装包逆向流水线
- [v0.3 增补](docs/03-界面改版与ComfyUI接入.md)：行式设置界面改版 + 本地 ComfyUI 出图
- [架构说明](docs/ARCHITECTURE.md)、[冒烟清单](docs/smoke-checklist.md)
- [第三方组件与素材声明](THIRD-PARTY-NOTICES.md)

## 许可

MIT，见 [LICENSE](LICENSE)。第三方组件的许可见「关于」页。

界面图标来自微软 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)（MIT）；应用图标为项目自绘（矢量绘制后打包 16–256 七个尺寸）。

本项目**只借鉴竞品的设计思路**，不包含任何竞品的代码或素材。
