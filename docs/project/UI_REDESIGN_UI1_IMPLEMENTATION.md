# UI 重构 UI-1 实施记录

日期：2026-11-02（Asia/Shanghai）

状态：DSH 双端 U01–U09 通过，U10 在修复后的旧 MAUI 隔离验证宿主补测通过。各项限制见验收记录。

DSH 首轮 `acceptance-20261102-2359` 包含 Windows 可见交互与 Android 真机证据。
2026-11-08，DSH 补测 U10 的系统文件选择、Merge 预览及取消。第 4 节记录 11-02
工程交付时的结果，最新验收结论及证据见 [验收修复记录](UI_REDESIGN_UI1_ACCEPTANCE_FIXES.md)。

## 1. 基线与范围

- 仓库 `E:\Users\14160\source\repos\FurinaChronicle`，`feat/phase-8 @ 5e2a4a5`。
- 版本源保持 `0.8.15 / 8C.6`；当前产物标记 `0.8.15.215+phase.8C.6.sha.5e2a4a5.dirty`。
- 当前数据库 Schema 11；没有改 Schema、迁移、Portable、身份、来源、覆盖、Undo、Tombstone 或 Chronicle 规则。
- 开始时已有未跟踪的 `Resources/Images/ItemRarity/`、`gacha_quality_1.png`、`gacha_quality_2.png`，原样保留，不纳入本包。
- 依据外部 UI 规划（2026-10-21）与 UI 续接计划（2026-10-23）。文中的后续阶段提示词作为规划上下文；本次用户授权范围为 UI-1。
- UI-0 的维护者双端反馈补记为 Maintainer reported，不重开已关闭的 8B，不替 8C 完成设备退出评审。

## 2. 已实现

`FurinaChronicle.Presentation` 只为双端实际共用的抽卡只读流程新增小型边界：

- `ServiceGachaReadOnlySource` 复用 `GetPlayerArchives`、`GetGameAccounts`、`GetGachaRecordPage`；明细每页 25 条，摘要使用同一查询服务的总数，不复制统计算法或加载全量记录计算计数。
- `GachaReadOnlyState` 是页面实例状态，保存档案／账号副本 GUID 和本页各档案的最后账号。没有全局当前账号，不使用原生产抽卡页的 Preferences 键；跨进程恢复选择不属于本包。
- 重新进入页面及切换范围时重新查询；名称更新按 ID 对齐，失效选择回退首个可用范围并说明原因。加载、无档案、无账号、无记录、失败、取消分开表达。
- 取消令牌与请求代数共同拦截迟到结果；UI 调度回调执行时再次核对页面生命周期。离开页面会取消并使旧结果失效。
- 图片仅共享稳定资源键和可选本地缓存路径；WinUI 在 converter 创建 `BitmapImage`，MAUI 在视图绑定边界创建平台图片源。没有图片或联网时，名称、星级、时间与来源仍可读。缓存键限制为 ASCII 字母数字，不能变成任意文件路径。
- `IUiDispatcher` 由 WinUI `DispatcherQueue`、MAUI `MainThread` 分别适配。共享项目不依赖 MAUI、WinUI、SQLite 或 Infrastructure。
- 只读纵切没有实际文件导入／导出需要，未预建 picker／流／文件保存框架；数据库、元数据、图像缓存和临时文件路径由平台组合根明确提供。

`Ui1PreviewEnvironment` 位于 Infrastructure 的诊断组合边界，使用正式建档、建账号和 `CommitGachaRecords` 服务生成可持久化的合成数据库，不把静态数组直接绑定为业务读取结果。初始化可重试、重开不重复新增记录；同一路径的预览会话持有排他文件租约。页面只读，种子生成是该独立环境的初始化行为。

| 合成范围 | 预期 |
|---|---|
| 01 合成档案 / A，虚构 UID 100000001 | 61 条，25 / 25 / 11 三页 |
| 01 / B，虚构 UID 100000002 | 7 条 |
| 01 / C，虚构 UID 100000003 | 0 条，账号空状态 |
| 02 同 UID 独立副本 / A，100000001 | 3 条，与 01 / A 为不同账号副本 |
| 03 空档案 | 无账号 |

合成条目名称明确带“合成物品”，日期为固定的 2026-10-01 +08:00，来源交替为未知／用户输入，不虚构官方证据。当前 Gacha 服务模型的发生时间是必填值，本轮不为其发明“未知发生时间”哨兵或修改时间语义。

Windows 在既有 WinUI 导航中增加默认的“抽卡只读预览”，保留 UI-0 组件、主题和材质入口。Android 在既有 MAUI 首页增加受控入口；另外提供只加载该页面的 `Ui1PreviewMode=true` 独立验收应用。两端列表在剩余空间独立滚动，无固定 560 高度或外层整页滚动。

## 3. 路径、设置与凭据

| 宿主 | 数据／缓存 | 选择／凭据 |
|---|---|---|
| 原 MAUI Windows／Android | 保持既有 `FileSystem.AppDataDirectory/furinachronicle.db3` 与原 Cache；已有 Phase 验收条件仍按原逻辑 | 原 `PreferencesArchiveSelectionStore` 与 `SecureStoragePassportAccountStore` 不变 |
| 正常 MAUI 内的 UI-1 页面 | `FileSystem.AppDataDirectory/UI1/Maui/ui1-preview.db3`；同目录 `metadata/images/temp` | 仅本页内存选择；预览流程不解析通行证服务。正常应用原启动流程仍存在，不能将其宣称为凭据隔离验收环境 |
| 独立 MAUI UI-1 验收包 | 独立 package 的私有 AppData 下同一固定 UI1/Maui 子目录 | 启动组合根只注册预览页，不注册生产数据库、Preferences、SecureStorage、通行证、网络元数据或生产启动服务 |
| 独立 WinUI | `%LOCALAPPDATA%/FurinaChronicle/UI1/WinUI/ui1-preview.db3`；同目录 `metadata/images/temp` | 不接入 Preferences／SecureStorage；只保存页面内选择 |

WinUI 保持无包身份、自包含开发预览，未改 MSIX／商店分发。Android 主应用标识不变；仅显式 `Ui1PreviewMode=true` 时为 `com.companyname.furinachronicle.preview.ui1`，并禁止与 8B／8C 验收模式同时启用。此包与主包可并存。

WinUI 可在进程创建前显式提供 `FURINA_UI1_DIRECTORY` 绝对基础目录，数据库名及
`UI1/WinUI` 子路径仍固定。本轮执行环境拒绝默认 LocalAppData 的创建权限，UIA 走查
使用仓库 `artifacts/ui1/windows-data`；没有修改目录 ACL 或生产路径以绕过限制。
初次发布还暴露了当前 WinUI 工具链未将应用 `.pri` 带入独立发布目录的问题；新增
`PublishPreviewResourceIndex` 显式发布生成的索引，并在缺失时使发布失败。
清理陈旧 XAML 元数据后确认索引含 MainWindow 与 Ui1GachaView，启动错误不能仅靠编译通过排除。

上述 MAUI 生产路径与存储机制来自当前代码审查，没有启动生产实例读取实际凭据。独立验收包通过组合根隔离敏感服务；未来真正接入主库前，仍需实际路径发现与只读核验、应用身份及系统凭据兼容检查、进程间单写者／互斥、SQLite 一致备份、未来版本保护，以及不能用旧客户端打开高 Schema 的明确回退条件。当前合成库的文件租约不等于已完成生产迁移方案。

## 4. 工程验证

针对性测试覆盖真实 SQLite 合成数据、重开与幂等、分页、同 UID 副本隔离、缓存路径拒绝越界、会话排他，以及页面独立选择、改名／删除回退、A→B 迟到、离开后回写、排队的 UI 回调、取消、失败重试与三类空状态。

全量、宿主构建与固定产物指纹见 [DSH 验收指南](../development/UI1_DSH_ACCEPTANCE_GUIDE.md)。首次受限测试的 6 个失败在正常权限下 631/631 复验通过；没有放宽断言。构建时 NuGet 访问与 MSBuild 任务宿主权限问题通过正常权限执行还原／构建解决，未改仓库 SDK 或包版本来绕过。

使用 `eng/ui1/Test-WindowsPreview.ps1` 可执行有限的 Windows UIA 走查。即使它通过，也只证明脚本涉及的原生控件与数据状态，不替代 DSH 的截图、键盘、窗口尺寸、字体和 Android 真机证据。

| 验证层级 | 本次结果 |
|---|---|
| 针对性自动化 | 15/15 通过 |
| 最终完整 Release 回归 | 631/631 通过，0 失败、0 跳过；TRX 位于 `artifacts/ui1-tests/ui1-regression.trx` |
| 独立 WinUI Release／自包含 publish | 通过；发布目录包含当前应用 `.pri`、许可与 NOTICE |
| Android 独立 UI-1 ARM64 Release APK | 通过，0 警告、0 错误；固定 APK 已复制到 `artifacts/ui1/android-arm64` |
| 正常 Android ARM64 Release | 通过，0 警告、0 错误；未用此主包执行真机验收 |
| 旧 MAUI Windows Release win-x64 | 通过，0 警告、0 错误；未启动生产业务实例 |
| Windows 进程启动 | 实际启动并取得窗口标题，随后关闭受控进程 |
| Windows 有限 UIA 交互 | 10 项检查 Pass；包括实际最新物品文字、真实摘要、分页、账号切换、同 UID 副本与原档案最后选择恢复、空账号及空档案；详见 `artifacts/ui1/windows-uia-smoke.json` |
| Windows 正常关闭 | Pass；关闭窗口后进程正常退出，未强制结束；详见 `artifacts/ui1/windows-close-smoke.json` |
| Windows 截图／完整键盘／缩放／辅助功能 | Not run，交 DSH 按指南补测 |
| Android 模拟器／真机 | Not run；本次 `adb devices -l` 未发现设备 |
| 原 MAUI 关键用例实际交互 | Not run；不能用编译通过代替 |
| 版权头／diff 空白检查 | 通过 |

构建采用仓库版本源，未提交更改，也没有声称已运行 GitHub Actions。Windows 隔离数据
目录访问失败与发布资源遗漏的初始失败证据保存在 `artifacts/ui1/diagnostics`；最终发布
目录不保留临时诊断记录或异常日志代码。

## 5. 保留到后续阶段

- UI-2：完整导航、范围与筛选体验，以及总览／明细／历史／日历／物品五种分析视图、主题与布局收口。UI-1 单列表不代表 UI-2 完成。
- UI-3：三种刷新、全部导入导出、预览、纠正、删除、Undo 与持久结果。
- UI-4～6：管理闭环、新领域页面、生产路径／凭据迁移、性能与辅助功能矩阵、默认 Windows 入口切换。
- 原 MAUI 业务页面仍是生产回归基线。U10 覆盖旧宿主的六个入口、隔离库空态及恢复预览取消；生产数据回归及恢复提交属于后续独立验收。
- 无新增 NuGet 包、第三方依赖或许可证；Windows App SDK 继续沿用 2.5.1。CI 的独立 WinUI 产物统一命名为 windows-preview，未改变正式版本源，也未将新宿主设为默认产品入口。
