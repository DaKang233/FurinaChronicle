# UI 重构 UI-0 实施记录

状态：自动化部分完成，Windows 与 Android 设备交互验收待完成

日期：2026-10-21

## 1. 范围与基线

本轮实施依据仓库外规划：

`E:\Workspace\FurinaWorkspace\FurinaChronicle_UI_Redesign_Plan_20261021.md`

读取时该文件为 UI Plan 1.0，共 933 行、67,880 字节。本仓库只引用其名称与来源，
没有复制第二份规划。实施前基线如下：

- 分支：`feat/phase-8`，跟踪 `origin/feat/phase-8`；
- HEAD：`257078cbbd8567ce00ccdd45e44d02a864874cfb`，提交
  `257078c 2026-10-21T17:49:20+08:00 Update README.md`；
- 工作区：干净；
- SDK：`.NET SDK 10.0.402`，由 `global.json` 的 `10.0.100` 与
  `rollForward=latestFeature` 规则选中；
- 应用版本源：`0.8.7`，阶段标签仍是 `8B.6`；UI-0 不改版本语义；
- 工作负载：Android `36.1.118`、MAUI Windows `10.0.110`；
- 现有 MAUI 目标：`net10.0-android` 与
  `net10.0-windows10.0.19041.0`；
- 数据库实现的当前 Schema 是 7。`docs/architecture/OVERVIEW.md` 仍写 Schema 6，
  属于实施前已有的文档漂移；本轮以 `FurinaDatabase.CurrentSchemaVersion=7`、
  `OPEN_QUESTIONS.md` 和 Phase 8B 记录为准，没有修改 Schema 或迁移器。

开始前已完整或按任务相关范围阅读 `AGENTS.md`、项目宪章、数据原则、架构总览、
页面选择状态、决策索引与全部 Accepted ADR、Phase 8B 当前记录、测试与版权头规范；
同时核对了解决方案、项目引用、CI、版本规则和参考原型
`E:\Users\14160\source\repos\FurinaUiPrototype`。原型只提供了内容优先、侧栏/工具栏、
操作聚合和响应式方向，没有复制其模拟数据、页面代码或类型名。

## 2. 决策边界

新增 [ADR 0020](../decisions/0020-platform-specific-ui-hosts.md)，并由维护者于 2026-10-21
接受。正式决定内容是：

- Windows 使用独立 `FurinaChronicle.Windows` WinUI 3 宿主；
- Android 继续使用现有 MAUI 宿主，只在必要处接入小型 Android 原生适配器；
- 直接复用 Core、Services、Infrastructure，不先建立大型 Presentation 框架；
- 平台文件、窗口、凭据、设置、图片与 UI 调度留在宿主或小型端口边界；
- 页面/领域范围选择继续按稳定 ID 独立保存与修复，不建立跨页面可变的全局当前账号；
- UI-0 不连接真实数据库或凭据。

UI-0 没有改变身份、来源、导入覆盖、删除、Undo、Chronicle generation、游标或公开格式
语义，也不代表 Phase 8B.7 完成。Phase 8B.7 所需的真实数据库、跨设备和文件往返证据
仍是独立验收项。

## 3. Windows 技术小样

### 3.1 宿主与部署选择

新增 `FurinaChronicle.Windows`：

- `Microsoft.NET.Sdk` + `UseWinUI=true`，不是 MAUI Windows 页面或 Handler；
- `net10.0-windows10.0.19041.0`，最低平台版本 `10.0.17763.0`；
- x64、`WindowsPackageType=None`，当前是无包身份的直接 EXE；
- `.slnx` 将解决方案的 `Any CPU` 与 `x64` 显式映射到该项目的 `x64`，避免
  VS 2026 尝试加载不存在的 `Debug|Any CPU` / `Release|Any CPU` 项目配置；
- Windows App SDK `2.5.1`，`.NET` 与 Windows App SDK 均自包含；
- 关闭 trimming 与 ReadyToRun，避免 UI-0 在缺少运行验收时引入额外发布变量；
- 引用现有 Core、Services、Infrastructure，但技术小样不启动其持久化组合根；
- Windows App SDK 的原始 `license.txt` 与 `NOTICE.txt` 自动复制到输出和发布物的
  `Licenses` 目录。

Windows App SDK 2.5.1 是实施日的稳定通道版本，而不是预览版。官方资料：

- <https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels>
- <https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1>
- <https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time>

CI 保留旧 MAUI Windows 产物，并新增明确标记为 UI-0 preview 的独立 WinUI 产物，避免
技术小样被误认为业务替代版本。

### 3.2 已实现的代表性能力

- 原生 `NavigationView` 壳层与三个 UI-0 导航状态；
- 原生 `CommandBar`、`AppBarButton`、`ListView`、`InfoBar`、`ContentDialog`、
  `MenuFlyout`、`ComboBox` 和 `ToggleSwitch`；
- F5、Ctrl+O、Ctrl+E 键盘加速键；
- 原生控件 Tab 导航、对话框/Flyout 的 Esc 生命周期及关闭后的入口焦点恢复；
- `RequestedTheme` 的跟随系统/浅色/深色切换，并显示实际主题；
- 请求 `MicaBackdrop`，捕获不可用异常，并提供可重复测试的强制系统纯色回退；
- 使用 WinRT `FileOpenPicker`，以当前 WinUI 窗口 HWND 完成关联；选择后只显示文件名，
  不读取内容；
- 五条完全虚构的列表记录；
- 显示但不创建隔离测试路径 `%LOCALAPPDATA%\FurinaChronicle\UI0\WindowsPreview`。

技术小样不注册数据库、Secure Storage、账号、网络或遥测服务。

## 4. Android 技术验证

### 4.1 实现方式

现有 MAUI Android 首页增加一个受平台条件约束的 UI-0 区域：

- 页面根容器与滚动区启用安全区域，内容没有固定页面高度；
- 按实际可用宽度显示 Compact（小于 600dp）、Medium（小于 840dp）或 Expanded，
  同时显示低高度状态；
- `AppThemeBinding` 和 `RequestedThemeChanged` 显示系统主题变化；
- 页面级扩展操作按钮通过自定义 MAUI View + Android Handler 创建真实
  `ExtendedFloatingActionButton`；
- 操作按钮打开真实 `BottomSheetDialog`，并取得 `BottomSheetBehavior` 设置 expanded、
  hideable 和 skip-collapsed；
- 对话框原生支持系统返回、遮罩取消与下滑隐藏，关闭事件统一完成异步生命周期；
- 面板包含原生 `NestedScrollView`、Material 按钮、多行 `EditText` 和足够的长内容；
- 窗口使用 `AdjustResize`；系统栏与 IME insets 共同决定底部内边距；
- 关闭后 MAUI 入口按钮重新启用并请求焦点；
- 所有操作只更新受控状态文字，不读取数据库、文件或网络。

这不是普通 `Border` 伪装的 BottomSheet。页面中保留的 Border 只负责说明和状态卡片，
实际弹层生命周期来自 Android Material 组件。

### 4.2 依赖、维护与替代方案

没有新增 Android 第三方包。MAUI 10.0.110 已解析：

- `Xamarin.Google.Android.Material 1.12.0.5`；
- 对应原生 `com.google.android.material:material 1.12.0`；
- NuGet 元数据声明 `MIT AND Apache-2.0`；
- 绑定由 Microsoft 的 AndroidX 仓库维护，当前包仓库提交为
  `68db082220ebc7f370f61bf8c64e4832db889928`。

因此 UI-0 直接使用已有依赖图，避免引入第二套弹层库。若设备验收发现 MAUI Activity
生命周期、无障碍或预测返回不能满足要求，后续替代顺序是：先完善同一原生适配器，
再评估维护活跃且许可兼容的专用组件；不因此整体迁移 Kotlin/Compose。

### 4.3 关键文件

- `FurinaChronicle.Windows/`：独立 WinUI 3 项目、应用、窗口、清单与 UI-0 样例；
- `FurinaChronicle.slnx`：接入新项目；
- `FurinaChronicle.App/UI/TechnicalPreview/`：最小跨平台入口 View 与 BottomSheet 端口；
- `FurinaChronicle.App/Platforms/Android/AndroidMaterialExtendedActionButtonHandler.cs`：
  Material 扩展操作按钮 Handler；
- `FurinaChronicle.App/Platforms/Android/AndroidMaterialBottomSheetPreview.cs`：
  原生 BottomSheet、返回/手势、滚动、键盘与 insets 生命周期；
- `FurinaChronicle.App/UI/Pages/HomePage.xaml(.cs)`：受控 Android UI-0 入口、主题与
  响应式状态；
- `FurinaChronicle.App/MauiProgram.cs` 与
  `Composition/PlatformServiceCollectionExtensions.cs`：按平台注册 Handler/端口；
- `docs/decisions/0020-platform-specific-ui-hosts.md` 与决策索引：Accepted ADR；
- `.github/workflows/build-and-test.yml`、`docs/development/CONTINUOUS_INTEGRATION.md`
  与 `THIRD-PARTY-NOTICES.txt`：CI、发布物与许可收口；
- 本文：实际证据、风险、未验证项和 UI-1 门槛。

## 5. 平台集成风险

| 项目 | 当前证据 | UI-1 前约束或验证 |
| --- | --- | --- |
| Windows 身份 | 新宿主当前 `WindowsPackageType=None`，无包身份，从 EXE 启动 | 商店/MSIX 身份、升级通道和最终 Application ID 未决；不得假设无包与有包数据/凭据路径相同 |
| 主数据库 | 旧 MAUI 组合根使用 `Path.Combine(FileSystem.AppDataDirectory, "furinachronicle.db3")` | 本轮未启动旧宿主，故未读取或记录当前机器的真实绝对路径；接入前要用隔离环境核对两宿主解析路径 |
| 元数据与缓存 | 元数据 DB、事件目录和图片/portable 缓存同样从 MAUI `FileSystem` 目录派生 | 提取小型存储路径端口，不能把 MAUI 静态 API带入新宿主 |
| 新宿主数据 | UI-0 只显示独立测试路径，未创建目录、数据库或迁移 | UI-1 仍应默认使用隔离测试库；真实库接入需另行审批与一致备份 |
| 凭据 | 旧宿主使用 `SecureStorage.Default`，键为 `passport-accounts-v2`，并兼容读取 v1 | 新宿主没有凭据适配器；包身份/无包身份、DPAPI 范围和存储位置兼容性均未实测，不得复制明文或假设可直接读取 |
| 文件选择器 | 新宿主已在代码中通过 HWND 初始化 `FileOpenPicker`；旧导出实现也有 MAUI Window → HWND 关联 | 新宿主仅做启动烟测，选择器取消、焦点返回和多窗口归属仍需可见窗口人工验证 |
| UI 调度 | 现有 ViewModel/页面包含 `MainThread`、`Shell.Current`、`Application.Current` | 迁移一条纵切时分别提取调度、导航/对话框端口；不要把 WinUI Dispatcher 或 MAUI MainThread 放入 Services |
| 图片 | Gacha 展示模型和 ViewModel 直接持有/创建 MAUI `ImageSource` | UI-1 优先共享路径/URI/加载状态，平台层生成各自图片对象 |
| 设置 | `Preferences.Default` 在 MAUI 组合根注册，页面选择状态依赖其接口 | 保留每页面独立键和修复规则；新宿主实现同一最小语义，不引入全局当前账号 |
| 同时运行 | UI-0 新宿主不打开真实库，因此当前无双写 | 后续真实接入前必须定义单写者/互斥、WAL 一致备份、版本保护和失败回退；不得让旧新客户端同时写同一库 |

Microsoft 对 MAUI Secure Storage 的平台说明见：
<https://learn.microsoft.com/dotnet/maui/platform-integration/storage/secure-storage>。
该文档只能说明框架机制，不能替代本应用身份和现有数据的隔离实测。

## 6. 构建、测试与证据等级

### 6.1 已通过的自动化

| 验证 | 命令摘要 | 结果 |
| --- | --- | --- |
| 新 WinUI Debug | `dotnet build FurinaChronicle.Windows ... -c Debug -r win-x64` | 通过，0 警告、0 错误，14.61 秒 |
| Android Debug | `dotnet build FurinaChronicle.App -f net10.0-android -c Debug` | 通过，0 警告、0 错误；首次完整构建 53.36 秒，最终增量复验 16.62 秒 |
| 新 WinUI Release | `dotnet build FurinaChronicle.Windows -c Release -r win-x64 -p:FurinaRequireReliableVersion=true` | 通过，0 警告、0 错误，21.52 秒；许可/NOTICE 已复制 |
| 新 WinUI publish | `dotnet publish FurinaChronicle.Windows -c Release -r win-x64 --self-contained true --no-restore` | 通过，12.50 秒；EXE、12,637 字节许可与 340,893 字节 NOTICE 均在 publish 目录 |
| Android Release | `dotnet build FurinaChronicle.App -f net10.0-android -c Release -p:FurinaRequireReliableVersion=true` | 通过，0 警告、0 错误，2 分 49.23 秒 |
| 旧 MAUI Windows Release | `dotnet build FurinaChronicle.App -f net10.0-windows10.0.19041.0 -c Release ...` | 通过，0 警告、0 错误，最终增量构建 7.96 秒 |
| VS 2026 解决方案映射 | `devenv FurinaChronicle.slnx /Build "Debug|Any CPU" /Project FurinaChronicle.Windows` | 通过；Windows 项目实际使用 `Debug x64`，4 个依赖项目成功、0 失败 |
| 共享测试 | `dotnet test FurinaChronicle.Tests -c Release` | 517 通过，0 失败，0 跳过，12 秒 |
| 版本规则 | `build/Verify-Versioning.ps1` | 通过 |
| 版权头 | `eng/copyright/Manage-CopyrightHeaders.ps1 -Mode Check` | 已跟踪的适用文件通过；新增文件另行在最终差异检查中核对 |

GitHub Actions 没有在本地伪装为已运行；本轮只完成工作流静态修改和等价本机构建。
VS 2026 的 MAUI 工具在这次诊断中仍曾探测其自带共享 Android SDK；这与已修复的
WinUI 解决方案配置映射不是同一个问题。仓库不硬编码开发机路径，后续本地 Android
命令按用户约定显式传入 `-p:AndroidSdkDirectory=E:\AndroidSDK`。

### 6.2 启动与设备证据

- **静态审查**：完成。
- **编译**：新 WinUI、Android、旧 MAUI Windows 的 Debug/Release 适用组合已通过。
- **自动化测试**：517/517 通过。
- **Windows 进程启动烟测**：通过。Release EXE 在隐藏、无 Shell 关联模式启动，PID
  `68708` 在 5 秒后仍存活并有非零主窗口句柄 `10818482`，随后只终止该受控进程。
- **Windows 实际可见交互**：未验证。隐藏启动不能证明导航、Mica、主题、键盘、Esc、
  焦点或文件选择器的人机行为。
- **Android 模拟器**：未验证。检查时无连接设备；本机虽配置 `Pixel_7_API_36` AVD，
  但本轮没有在无法确认其应用数据隔离状态时启动并覆盖安装。
- **Android 真机**：未验证。

因此准确状态是“自动化部分完成，设备交互验收待完成”，不是 UI-0 全部验收完成。

## 7. 人工验收清单

### 7.1 Windows

1. 从 Release 或 CI 的 `windows-ui0-preview` 目录直接启动 EXE，确认无需安装运行时。
2. 在 Windows 10（纯色回退）与 Windows 11（Mica）各测试一次；切换系统透明效果、
   高对比度、浅色/深色，并确认所有文字与控件仍可读。
3. 用 Tab、Shift+Tab、方向键、Enter/Space 遍历导航、命令、列表和右侧表单；核对焦点
   顺序与可见焦点环。
4. 打开 `ContentDialog` 和 Flyout，以 Esc、按钮和外部点击关闭，确认焦点回入口。
5. 使用 F5、Ctrl+O、Ctrl+E；文件选择器分别取消和选一个非敏感 JSON/ZIP，确认窗口
   归属正确且样例没有读取内容。
6. 切换强制纯色后重复导航和弹层，确认 Mica 不可用不影响功能。
7. 缩窄、放大窗口并使用 100%/150%/200% 缩放；记录截断、滚动和最小可用宽度。

### 7.2 Android

1. 在全新模拟器快照或明确可清理的测试设备安装 Release APK；不要覆盖含真实数据的
   既有 FurinaChronicle 安装。
2. 从首页打开扩展操作按钮，分别用关闭按钮、遮罩、系统返回键/返回手势和下滑关闭；
   每次确认入口恢复可用和焦点。
3. 打开键盘，输入多行文字并滚动到最后一项；确认输入框、操作按钮和底部内容不被 IME、
   状态栏或导航栏遮挡。
4. 切换三键/手势导航、浅色/深色、最大系统字体和显示缩放；关闭并重新打开面板确认主题。
5. 在 Compact/Medium/Expanded 宽度与横屏、低高度下重复步骤 2～4。
6. 使用 TalkBack 核对入口、关闭按钮、三个操作和输入框的名称、顺序及关闭后焦点。
7. 记录 Android 版本、设备/AVD、导航模式、字体/显示比例、结果及截图；截图不得包含真实
   账号或数据。

## 8. 与规划的偏差及未完成项

- 规划基线 HEAD、Schema 7、MAUI 10 和 Phase 8 状态仍准确；未发现需要反向采用旧
  Schema 或历史版本号的情况。
- 规划未锁定 Windows App SDK 版本；实施选择当日稳定版 2.5.1，并记录许可和发布方式。
- 没有新增 `FurinaChronicle.Presentation`。UI-0 没有真实业务纵切，提前建项目会制造未被
  复用证明的抽象；此项符合“按实际需求提取”的约束。
- Android 没有增加第三方 BottomSheet 包，而是复用 MAUI 已有的 Microsoft Material
  绑定；替代方案保留到设备行为不合格时再评估。
- 尚未完成可见 Windows 交互、Android 模拟器或真机验收；不能把编译、隐藏启动或静态
  代码审查当作设备证据。
- 新 Windows 宿主没有业务页面、登录、真实导入、数据迁移或凭据读取，这是 UI-0 的
  有意边界，不是遗漏 UI-1～UI-6 的提前实现。

## 9. UI-1 进入条件与建议范围

进入 UI-1 前应满足：

1. ADR 0020 已于 2026-10-21 由维护者接受；后续实现须遵守其平台边界。
2. 完成上述 Windows 与 Android 人工验收，任何原生返回、焦点、insets 或主题缺陷先在
   UI-0 边界内修复。
3. 在全新隔离环境记录旧 MAUI Windows 的实际 AppData、Cache 和 Secure Storage 行为，
   决定新宿主身份/打包模式；不读取或迁移生产凭据。
4. 定义真实数据库接入前的单写者、互斥、一致备份、未来版本保护和回退验收。
5. 选一条只读或低风险抽卡纵切，提取最小页面状态、UI 调度、文件和图片端口；共享稳定
   ID 与业务语义，不共享 XAML、导航容器或平台图片对象。
6. 保留旧 MAUI Windows 回归构建；UI-1 不删除现有页面和服务，也不顺带完成
   Phase 8B.7、修改 Schema 或发布格式。
