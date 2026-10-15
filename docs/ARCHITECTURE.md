# 桌巢架构说明

## 三层 + 可选模块

```
DeskNest.App（WPF 界面）
  ├ 主窗口（行式设置页：图标 + 标题 + 说明 + 右侧控件）
  ├ 桌面层：收纳盒窗口、组件窗口、桌宠窗口（挂在 WorkerW 之下，不抢焦点）
  ├ 搜索面板（Alt+Space）、托盘、热键宿主
  └ 页面：概览 / 收纳盒 / 整理与规则 / 桌面组件 / AI 出图 / 外观 / 模块 / 数据 / 关于 …

DeskNest.Core（无 UI 依赖，可单独测试）
  ├ Models + Repository + Db（SQLite，WAL）
  ├ FileOps          文件安全操作：同卷 rename、跨卷 copy+校验+delete、重名 `(1)`、还原到原位置
  ├ OrganizeEngine   计划/执行分离：先出计划给用户预览，再执行并记录完整移动清单（可回滚）
  ├ RulesEngine      条件判定 + 默认文件分类表
  ├ SearchService    全盘索引（排除规则按"相对路径"匹配）+ 索引持久化
  ├ ComfyUiClient    本地 ComfyUI 客户端（连通性、模型列表、文生图工作流、轮询结果、下载图片）
  ├ ApiPanelService  模型 Key（DPAPI 加密）、调用统计、OpenAI 兼容 chat 调用
  ├ AiBridgeService  只读本地 HTTP 接口（127.0.0.1 + 令牌 + 逐项授权）
  ├ UsageStatsService 前台窗口采样 → 会话归并 → 报表
  ├ PerfSampler      性能采样（系统指标由 Native 提供）
  ├ PowerScheduler   定时/倒计时/空闲状态机（执行动作交给 Native）
  ├ BackupService    备份 / 回滚 / 诊断包
  └ PetEngine/PetPack 桌宠状态机与素材包

DeskNest.Native（Win32 互操作）
  DesktopLayer（Progman/WorkerW 挂载、非激活样式）、DesktopIcons、ShellIcons、
  GlobalHotKey、KeyboardHook（只暴露"有按键"事实）、ForegroundWatcher、
  SystemMetrics（GetSystemTimes / GlobalMemoryStatusEx / 网络 / nvidia-smi）、
  PowerActions（ExitWindowsEx + 回退 shutdown.exe）、DpapiSecretBox、AutoStart、Monitors
```

依赖方向固定为 `App → Core`、`App → Native → Core`；Core 不引用 WPF / WinForms。

## 可选模块

每个可选功能是一个独立工程（`DeskNest.Module.*`），编译出的 dll 由 MSBuild 拷到 `modules\`：

| 模块 | 作用 |
| --- | --- |
| `DeskNest.Module.ComfyUI` | 本地 AI 出图 |
| `DeskNest.Module.Pet` | 桌宠 |
| `DeskNest.Module.UsageStats` | 应用使用统计 |
| `DeskNest.Module.PerfMonitor` | 性能监视 |
| `DeskNest.Module.ApiPanel` | API 面板 |
| `DeskNest.Module.PowerSchedule` | 计划关机 |
| `DeskNest.Module.AiBridge` | 本地 AI 接口 |

加载方式：`ModuleHost` 扫描 `modules\` → 用独立 `AssemblyLoadContext` 加载 → 找到实现 `IDeskNestModule` 的类型 → 注入 `IModuleContext`（只暴露 Repository / Settings / 少数服务）→ 按启用状态回调 `OnEnabledChanged`。

模块缺失时：不加载、不运行、界面不出现（主窗口按"模块是否已加载"决定注册哪些页面）。

## 数据

- 数据目录：`%LOCALAPPDATA%\DeskNest`（`DESKNEST_DATA_DIR` 可覆盖）
- `desknest.db`：SQLite（盒子、条目、规则、整理历史、组件、待办、随记、桌宠、使用统计、API 配置与统计、搜索索引、意图记录 journal）
- `settings.json` + `.bak`：设置双写，损坏自动回退
- `logs\desknest.log`：运行日志（超过 4 MB 轮转）
- `backups\`：备份 zip
- `pets\<id>\`：桌宠素材包

## 关键设计

1. **计划/执行分离**：整理先生成计划（可预览），执行时记录完整移动清单 → 回滚是"按清单反向移动"，幂等。
2. **意图记录 journal**：真正动文件前先写一条记录，崩溃后可审计。
3. **重活离开 UI 线程**：图标/缩略图提取、索引、复制都有后台线程；界面只做渲染。
4. **性能预算**：空闲 CPU < 1%、内存 < 150 MB、热键到面板 < 200 ms（另有内置性能面板留档）。
5. **不注入**：桌面层用自己的窗口挂到 WorkerW，卸载即干净；键盘跟随是唯一的低级钩子，默认关闭且只取"有按键"这个事实。
6. **只读对外接口**：AI 接口仅监听 `127.0.0.1`，必须带 `X-DeskNest-Token`，并按数据类型逐项授权。
