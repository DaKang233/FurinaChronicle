# ADR 0020：平台专属 UI 宿主与最小展示边界

状态：Accepted

接受日期：2026-10-21

日期：2026-10-21

## 背景

现有 `FurinaChronicle.App` 是同时面向 Android 和 Windows 的 .NET MAUI
宿主。Phase 8 已形成可复用的 Core、Services 与 Infrastructure，但当前 Windows
页面仍受 MAUI Shell、跨平台控件密度和平台服务组合方式约束。Android 现有业务入口、
文件流程和部署链已建立，不适合仅为视觉重构同时迁移到另一套语言和 UI 技术栈。

UI 重构还必须保留页面局部的数据范围选择、档案内账号副本身份、导入覆盖、修订、
删除、Undo、Chronicle generation 与公开格式语义。UI 技术选择不得引入跨页面可变的
全局“当前游戏账号”，也不得使平台对象进入 Core 或 Services。

## 选项

1. 继续使用一个 MAUI 宿主，通过 Windows Handler 和样式模拟 Fluent 桌面体验。
2. Windows 新建独立 WinUI 3 宿主，Android 保留 MAUI；两端直接复用业务层，并只按
   已证明的真实需求提取平台无关展示逻辑与小型平台端口。
3. Windows 与 Android 同时迁移到新的原生技术栈，并重写全部展示层。

## 决定

采用选项 2：

- 新建 `FurinaChronicle.Windows`，使用稳定通道 Windows App SDK、WinUI 3 与原生
  Fluent 控件。它是独立进程和独立项目，不是 MAUI Windows 页面或 Handler。
- Android 暂时保留 `FurinaChronicle.App`，用 MAUI 页面承载内容，并在确有需要时通过
  小型 Android 适配器接入 Material 原生交互。
- Core、Services 与 Infrastructure 保持现有职责。平台宿主负责组合根、窗口、文件、
  凭据、设置、图片解析、UI 线程调度和导航。
- 不预先建立大型 `Presentation` 框架。共享展示状态只有在一条真实业务纵切证明两端
  具有相同语义和生命周期后才提取；MAUI `ImageSource`、`Shell`、`FileResult`、WinUI
  `Window` 和平台 DTO 不得进入共享层。
- 页面选择继续由页面或领域独立保存，并在页面重新进入时按稳定 ID 重查和修复；
  不新增跨页面可变的全局账号选择。
- UI-0 的两个技术小样只使用虚构或受控数据，不连接、迁移或清理真实数据库和凭据。

## 原因

独立 WinUI 3 宿主可以直接验证 NavigationView、Mica、系统主题、键盘、焦点和窗口相关
API，而不把 Windows 体验限制在 MAUI 页面树内。Android 保留现有宿主可以复用已经
工作的部署、导航和业务入口，并以较小风险验证 Material BottomSheet、返回、insets、
键盘与响应式行为。按纵切提取展示逻辑可避免先建设未经实际复用证明的抽象框架。

## 后果

- 两个前端共享业务语义，但分别维护 XAML、导航容器、窗口生命周期和视觉资源。
- Windows 需要独立确定应用身份、打包/分发模式、数据路径、凭据实现和文件选择器
  窗口关联；MAUI 的默认平台服务不能直接假定可复用。
- 迁移期间旧 MAUI Windows 宿主继续保留并构建，直到关键功能追平和真实数据切换通过
  验收。
- 迁移开发期不得让新旧 Windows 客户端同时写同一真实数据库。正式切换前需要 SQLite
  一致备份、路径与凭据兼容验证，以及明确的回退边界。
- 若后续发现两端无法稳定共享某段展示逻辑，应保留平台实现并共享底层用例，不以
  “共享率”作为正确性目标。

## 迁移影响

UI-0 不修改数据库 Schema、迁移器、公开格式、身份、导入、删除、撤销或 Chronicle
语义。新 Windows 宿主最初不打开生产数据库，也不读取旧凭据。后续接入真实数据前，
必须另行评审并实现路径发现、只读核验、备份、单写者/互斥及凭据安全迁移方案。

维护者于 2026-10-21 接受本 ADR。当前技术小样提供构建、启动和交互证据；
Accepted 状态冻结技术路线，但不将尚未完成的设备交互验收视为已完成，也不授权提前进入 UI-1。

## 测试要求

- 独立 WinUI 3 宿主能够构建和启动，并验证 NavigationView、系统主题、Mica 纯色回退、
  原生列表/命令/弹层/状态提示、键盘导航、Escape 和焦点恢复。
- Android 技术样例使用真实 Material BottomSheet 或行为等价的成熟组件，验证遮罩、
  系统返回/手势、内部滚动、键盘和系统 insets；普通 Border 不算通过。
- 旧 MAUI Windows 与现有 Android 目标继续构建，受影响的自动化测试通过。
- 自动化、编译、Windows 实际启动、Android 模拟器和 Android 真机证据分别记录；
  任一层级不得替代更高层级验收。
- 测试和文档只使用虚构标识，不访问或记录真实数据库内容与凭据。
