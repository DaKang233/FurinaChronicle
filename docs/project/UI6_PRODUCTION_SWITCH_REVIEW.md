# UI-6 生产切换评审包（预研）

状态：**Draft for review（未授权切换）**
日期：2026-11-13（Asia/Shanghai）
基线：`master 7ae4fe3`（`0.10.0 / 10.0`；Phase 10 Closed；UI-0～UI-5 已合并；文档声明 Release 回归 958/958）
性质：只读预研。本轮**未启动任何应用、未运行迁移/写库命令、未复制或读取生产数据内容、未接触生产凭据、未修改代码**；仅生成本文与 `artifacts/ui6-recon/` 下的路径清单证据（Git 忽略）。

> **本文件不构成切换授权。** ADR 0020 要求生产库/凭据迁移与默认入口切换必须另行评审并由维护者确认（`docs/decisions/0020-platform-specific-ui-hosts.md:53-59、63-70`）。本文只提供事实、选项、风险与决策清单；最终选择不由实现 Agent 决定。

---

## 0. 结论摘要

1. **生产数据是活跃的，且必须按“用户真实数据”对待。** 本机只读实测：Windows MAUI 生产 app-data 共 **575 个文件 / 255,153,285 B**；业务库 `Data\furinachronicle.db3` **81,596,416 B**，最后写入 2026-11-12T02:34:21Z（晚于 11-08 的既有基线 81,379,328 B）。**未打开/未复制/未计算哈希**。
2. **生产路径已实测定位，但依赖应用标识。** MAUI Windows 非打包存储为 `%LOCALAPPDATA%\<PublisherDisplayName>\<ApplicationId>\{Data,Cache,Settings}`，本机为 `%LOCALAPPDATA%\User Name\com.companyname.furinachronicle.app\`；`User Name` 来自 `Platforms/Windows/Package.appxmanifest` 的模板占位 PublisherDisplayName。**切换前若改 Publisher/ApplicationId/包装方式，数据目录会变化，表现为“数据丢失”**。
3. **凭据不能文件级复制。** MAUI Windows 用 `Settings\securestorage.dat`（`DataProtectionProvider`，descriptor 含 `LOCAL=user`）；WinUI 隔离宿主用 `UI4/WinUI/credentials/credentials.bin`（`ProtectedData` + 固定 entropy）。两者格式、字段与保护方式不同；**建议要求用户重新授权**，而不是无感导出（见 §2.4/§5-D4）。
4. **当前没有任何跨进程单写者机制。** `FurinaDatabase` 的 `.initialize.lock` 只在初始化期间持有（`FurinaDatabase.cs:177-198`）；`ArchiveRestoreWriterGate` 是进程内 static 信号量（`ArchiveRestore.Models.cs:11`）；SQLite 仅 `FullMutex` + 5 秒 busy timeout。**旧 MAUI 与新 WinUI 同时打开同一库在技术上可行**，必须补单实例/锁；ADR 0020 明令禁止迁移期双写。
5. **WinUI 的功能尚未与 MAUI 逐平，直接切换会带来功能回归。** 独立 WinUI 当前**没有**：角色/武器/挑战/螺旋的在线采集（UI-5 三个领域页只读）、米游社/HoYoLAB 登录（仅手动 SToken 粘贴）、Snap.Metadata 显示元数据（名称/图标）、Scoped Replace（仅 Merge）、恢复点回切（只读摘要）。切换前必须显式决定“接受回归并保留旧宿主”还是“先补功能”。
6. **推荐的最小风险路径（供评审，不是决定）**：复制迁移（SQLite 备份 → 新路径，源库保留）＋用户重新授权凭据＋单实例与 session lock 双保险＋迁移强制一致备份＋旧宿主保留一个发布周期。**默认入口、迁移触发、凭据策略、打包方式、旧宿主保留期必须由维护者决策（§5）。**
7. **UI-6 不应改 Schema/公开格式/身份/Undo/Chronicle 语义。** 当前业务库 Schema 12（`FurinaDatabase.cs:12`，application id `0x46554348`）；ADR 0020 只授权迁移实现，不授权格式或语义变更（`:65-67`）。

---

## 1. 现状盘点

### 1.1 仓库基线与已交付范围

| 项 | 值 | 证据 |
|---|---|---|
| HEAD | `7ae4fe3 docs(ui): record the ui-4 backup/credential and ui-5 winui increments` | `git log` |
| 版本源 | `0.10.0 / 10.0` | `Version.props:14-17` |
| 业务库 Schema | 12（application id `0x46554348`） | `FurinaDatabase.cs:12、34` |
| 测试 | 958（文档声明；属性计数 952 + `MemberData` 6） | `UI_REDESIGN_UI4_IMPLEMENTATION.md:137`、`UI_REDESIGN_UI5_IMPLEMENTATION.md:87`；本报告 §7.1 |
| UI-0～UI-3 | 已交付（独立 WinUI 抽卡完整数据操作） | `UI_REDESIGN_UI3_IMPLEMENTATION.md` |
| UI-4 | 已交付：组合根/壳/概览/档案账号/操作历史/设置/备份恢复 Merge/登录账号与 SToken（隔离库） | `UI_REDESIGN_UI4_IMPLEMENTATION.md:11-156` |
| UI-5 | 已交付：Chronicle 展示层 + MAUI 页；WinUI Chronicle/角色武器/挑战页（只读） | `UI_REDESIGN_UI5_IMPLEMENTATION.md:11-96` |
| 生产切换明确留待 UI-6 | 生产库发现、备份、凭据迁移、默认入口切换 | `UI_REDESIGN_UI4_IMPLEMENTATION.md:151-156` |

### 1.2 Windows MAUI 生产存储（本机只读实测）

> 实测方式：`Get-ChildItem` 列目录 + `FileInfo` 大小/时间；**未打开任何数据库、凭据或日志内容**。相对路径清单见 `artifacts/ui6-recon/production-appdata-current.json`（Git 忽略；不含文件内容、不含绝对用户目录）。

| 类别 | 路径（可复现形式） | 实测/代码证据 | 切换时如何处理（建议） |
|---|---|---|---|
| 应用数据根 | `%LOCALAPPDATA%\User Name\com.companyname.furinachronicle.app\` | 本机实测存在，含 `Data`/`Cache`/`Settings`；同父目录还有旧 ApplicationId 目录（见下） | 迁移探测源；冻结标识 |
| AppDataDirectory | `<root>\Data`（MAUI `FileSystem.AppDataDirectory`） | 代码 `PersistenceServiceCollectionExtensions.cs:61-63` 用 `Path.Combine(FileSystem.AppDataDirectory,"furinachronicle.db3")`；实测 DB 位于 `Data\furinachronicle.db3` | 迁移数据源 |
| **业务数据库** | `<root>\Data\furinachronicle.db3`（81,596,416 B；mtime 2026-11-12T02:34:21Z） | 实测；代码同上 | 复制迁移/一致性备份 |
| 初始化锁 | `<root>\Data\furinachronicle.db3.initialize.lock`（0 B） | 实测；`FurinaDatabase.cs:177-198` 只在 `InitializeAsync` 期间持有 `FileShare.None` | 不能当跨进程互斥 |
| Gacha 元数据库 | `<root>\Data\genshin-metadata.db3`（471,040 B） | 实测；`GachaServiceCollectionExtensions.cs:79` | 可选复制（缓存类） |
| YSHelper 事件目录 | `<root>\Data\metadata\yshelper-gacha-events.zh-Hans.json`（114,023 B） | 实测；`GachaServiceCollectionExtensions.cs:97` | 可选复制 |
| Snap.Metadata 缓存 | `<root>\Data\metadata\SnapMetadata\{current-version.txt,last-check.txt,versions\<SHA16>\...}` | 实测；`PresentationServiceCollectionExtensions.cs:70-72` | 可选复制；可能被 WinUI 重建 |
| Gacha 图标缓存 | `<root>\Cache\gacha-item-icons\`（223 文件） | 实测；`GachaServiceCollectionExtensions.cs:104` | 可重建，无需迁移 |
| 卡池横幅缓存 | `<root>\Cache\gacha-banner-images\`（318 文件） | 实测；`:108` | 可重建 |
| Portable 索引/暂存 | `<root>\Cache\gacha-portable-{index,staging}\` | 实测；`:120-130` | 临时，可清理 |
| Archive 导出快照 | `<root>\Cache\archive-export-snapshots\` | 实测；`ArchiveServiceCollectionExtensions.cs:23-26` | 临时，可清理 |
| Archive 恢复会话 | `<root>\Cache\archive-restore-sessions\` | 实测；`:27-30` | 临时，可清理 |
| **Replace 恢复点** | `<root>\Data\archive-recovery\`（当前不存在 = 无恢复点） | 代码 `SqliteGachaAtomicChangeStore.ArchiveRestore.Recovery.cs:95-115`（`<db dir>/archive-recovery`） | 随库迁移或明确放弃（需用户知情） |
| MAUI Preferences | `<root>\Settings\preferences.dat`（3,044 B） | 实测；MAUI Essentials 10.0.110 Windows DLL 字符串 `UnpackagedPreferencesImplementation`、`Settings`、`preferences.dat` | 选择状态可重建；便携设置需迁移 |
| **MAUI SecureStorage** | `<root>\Settings\securestorage.dat`（4,051 B） | 实测；DLL 字符串 `UnpackagedSecureStorageImplementation`、`DataProtectionProvider`、`ProtectAsync/UnprotectAsync`、`LOCAL=user`、`#securestorage.dat` | **不复制**；重新授权或显式迁移 |
| 崩溃日志（MAUI） | `%LOCALAPPDATA%\FurinaChronicle\Logs\windows-unhandled.log` | 代码 `Platforms/Windows/App.xaml.cs:26-47`；实测存在（14,564 B，未读内容） | 迁移前清理/保留由用户决定；脱敏评审 |
| 旧 ApplicationId 数据 | `%LOCALAPPDATA%\User Name\com.companyname.furinaarchive.app\Data\furinaarchive.db3`（20,480 B；2026-07-19） | 实测存在 | 探测并提示；不自动迁移（内容未读） |
| 非生产预览库 | `<root>\Data\UI1\Maui\ui1-preview.db3`（454,656 B，含 session.lock） | 实测；UI-1 页面写入生产目录 | 迁移时排除/提示清理（不是业务数据） |

**实测汇总**：`Data` 18 文件、`Cache` 555 文件、`Settings` 2 文件，合计 575 文件 / 255,153,285 B。
**与既有证据的偏差**：2026-11-08 的 U10 证据记录生产 app-data 554 文件 / 265,391,227 B、DB 81,379,328 B（`artifacts/ui1/fixes-20261103/u10-dsh-retest/REPORT.md:36、114-124`）；当前 DB 增长 217,088 B 且 mtime 为 2026-11-12T02:34:21Z，说明**生产实例在 Phase 9/10 工作期间仍被真实使用**（SnapMetadata 缓存 `HardChallengeSchedule.json` 时间同为 2026-11-12T02:34:22Z）。迁移前必须重新采样，不能沿用旧基线。

#### 1.2.1 生产路径的派生规则（代码 + 实测）

- `FileSystem.AppDataDirectory`（MAUI）→ `<root>\Data`；`FileSystem.CacheDirectory` → `<root>\Cache`（`PlatformStoragePaths.cs:10-29`；DB 代码路径印证）。
- `<root>` 的命名来自 MAUI Windows 生成清单：`Platforms/Windows/Package.appxmanifest` 定义 `Identity Name="maui-package-name-placeholder"`（构建后替换为 `com.companyname.furinachronicle.app`）与 `Publisher="CN=User Name"`、`PublisherDisplayName>User Name`（源文件 `Package.appxmanifest:11-17`；构建产物 `obj/Debug/.../resizetizer/m/Package.appxmanifest` 中为 `Identity Name="com.companyname.furinachronicle.app"`、`PublisherDisplayName>User Name`、`Version="0.10.0.272"`）。
- 结论：**数据目录 = 应用标识的产物**。冻结 `Publisher`/`ApplicationId`/`Package.appxmanifest` 是切换前置；若要改，必须先设计“旧路径 → 新路径”的一次性迁移（见 §2.6）。
- `Settings` 目录不是仓库代码创建：来自 MAUI Essentials 的非打包 Preferences/SecureStorage 实现（DLL 字符串证据见上），仓库只通过 `IPreferences`/`ISecureStorage` 访问（`PlatformServiceCollectionExtensions.cs:14-22`）。

#### 1.2.2 生产库的 Schema/标识（代码基线，未读取生产库）

- 代码常量：`CurrentSchemaVersion = 12`、`CurrentApplicationId = 0x46554348`（`FurinaDatabase.cs:12、34`）；初始化时按 application id + `user_version` 分派，未知/更高版本抛 `FurinaDatabaseCompatibilityException` 并保留原库（`:85-166`）。
- **本轮未读取生产库的 `PRAGMA application_id/user_version/quick_check`**；实施阶段必须先做只读核验（§1.3 步骤 4），且不得写入。

### 1.3 生产路径发现顺序（建议实现，只读）

1. **读版本与宿主**：`ApplicationVersionInfo`（MAUI）/`WindowsVersionInfo`（WinUI）确认构建与阶段；记录 commit/dirty，避免用旧版工具打开新库。
2. **解析应用标识**：从 `Platforms/Windows/Package.appxmanifest` 语义得到 `PublisherDisplayName` + `ApplicationId`；不要硬编码 `User Name`。
3. **候选根目录**（按优先级）：
   - a. `%LOCALAPPDATA%\<PublisherDisplayName>\<ApplicationId>`（本机实测存在）；
   - b. `%LOCALAPPDATA%\<ApplicationId>`（其他打包/历史布局可能的变体）；
   - c. 旧 ApplicationId（本机实测 `com.companyname.furinaarchive.app`）；
   - d. 显式覆盖（仅验证/开发构建使用，如 `FURINA_*_DIRECTORY`，不得成为生产默认）。
4. **只读校验**（任何写入前）：
   - 以 `SQLiteOpenFlags.ReadOnly` 打开 `<candidate>\Data\furinachronicle.db3`；
   - 校验 `PRAGMA application_id == 0x46554348`、`PRAGMA user_version == 12`（未来版本/未知 id → 拒绝并提示）；
   - `PRAGMA quick_check`（只读）；
   - 记录文件大小/mtime；哈希仅在用户授权并用于备份核对时计算（不得上传）；
   - **不得**在探测阶段 `CreateDirectory`/建库/迁移；WinUI 需复用“路径守卫 + 惰性环境”模式（`WindowsStartupProbe.cs:10-40`、`EnvironmentHolder.cs`）。
5. **探测结果**：`未找到`（引导创建新库/设置页手动选择）、`找到且兼容`（可迁移/接管）、`找到但不兼容`（拒绝写入并说明）、`找到旧 ApplicationId 数据`（提示用户）。
6. **单实例/锁**：打开生产库前先取得数据目录级租约（§2.3）；失败显示“另一个 FurinaChronicle 正在使用数据”而不是静默等待。

### 1.4 独立 WinUI 隔离宿主现状（对照）

| 项 | 现状 | 证据 |
|---|---|---|
| 数据根 | `%LOCALAPPDATA%\FurinaChronicle`（或 `FURINA_UI1_DIRECTORY`）；当前实测该根下仅 `Logs\windows-unhandled.log`（14,564 B），无 UI4 数据（UI-4/5 验收使用独立 `FURINA_UI1_DIRECTORY`） | `Composition/WindowsPaths.cs:10-49`；实测 |
| 数据库 | `<base>\UI1\WinUI\ui1-preview.db3`，**合成种子**、`session.lock` 生命周期租约 | `Ui1PreviewEnvironment.cs:37-46、131-140、165-190` |
| 设置 | `<base>\UI4\WinUI\settings\{ui-preferences.json, archive-selection.json}` | `WindowsPaths.cs:33-34`、`WindowsSettingsPorts.cs:13-18` |
| 凭据 | `<base>\UI4\WinUI\credentials\credentials.bin`；`ProtectedData`(CurrentUser) + entropy `FurinaChronicle.UI4.WinUI.Credentials.v1`；Payload v1 仅 SToken/Mid/Aid/DeviceId/标签/时间/可用状态 | `DpapiPassportCredentialStore.cs:13-22、282-329、339-356` |
| 生产路径发现 | **显式不做**（注释与启动探针都声明）；SToken/网页缓存占位类仍保留在环境中，但 UV-4 组合根另组真实 SToken provider | `WindowsPaths.cs:6-7`、`WindowsStartupProbe.cs:10-12`、`Ui1PreviewEnvironment.cs:227-247`、`WindowsComposition.cs:134-149` |
| 导航/页面 | 概览、游戏历程、抽卡、角色武器、周期挑战；管理区档案账号、操作历史；页脚登录账号、设置；备份恢复为概览/档案页入口 | `MainWindow.xaml:26-48`、`MainWindow.xaml.cs:300-360` |
| 窗口 | 固定初始 1100×720；**无位置恢复**；**无第二实例激活/单实例** | `MainWindow.xaml.cs`（`ResizeInitialWindow`）；`UI_REDESIGN_UI4_IMPLEMENTATION.md:85、155` |
| 打包 | `WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`、`SelfContained=true`、无 MSIX、无安装器 | `FurinaChronicle.Windows.csproj:9-28`、`app.manifest` |

### 1.5 生产 MAUI 与 WinUI 隔离宿主差异总表

| 维度 | MAUI Windows（生产） | WinUI（隔离） | 切换影响 |
|---|---|---|---|
| 数据目录 | `%LOCALAPPDATA%\User Name\com.companyname.furinachronicle.app\{Data,Cache,Settings}` | `%LOCALAPPDATA%\FurinaChronicle\UI1\WinUI\...`（或环境变量覆盖） | 必须迁移/接管 + 冻结标识 |
| 数据库 | 真实库 81.6 MB，Schema 12（代码基线） | 合成库，Schema 12，带种子 | 迁移需备份/校验 |
| 凭据 | SecureStorage 单文件（`LOCAL=user` + 全字段 JSON） | DPAPI 文件（`ProtectedData`+entropy，字段子集） | 不兼容，重新授权/显式迁移 |
| 设置 | `Settings\preferences.dat`（MAUI Preferences 键） | `<base>\UI4\WinUI\settings\*.json` | 选择可重建；主题/便携设置需映射 |
| 恢复点 | `<Data>\archive-recovery`（当前无） | 随隔离库（测试用） | 迁移/放弃需用户知情 |
| 元数据/图标 | `Data\genshin-metadata.db3`、`Data\metadata\...`、`Cache\gacha-*` | 隔离 `metadata/images`，且**未接 Snap.Metadata 服务** | WinUI 名称/图标会退化 |
| 单文件备份 | 无自动备份；用户可导出 `.fca` | 无 | 需新增迁移备份 |
| 日志 | `%LOCALAPPDATA%\FurinaChronicle\Logs\windows-unhandled.log`（未脱敏异常全文） | 启动 shell 失败时写 `%TEMP%\furina-ui4-buildshell-error.txt`（异常全文，无脱敏） | 需统一脱敏/保留/清理策略 |

### 1.6 迁移前必须补齐的功能差距（parity）

> 这些差距决定“直接切换”是否可接受；不是 UI-6 的实现内容，但必须在决策中显式记录。

| # | 能力 | MAUI Windows | WinUI 现状 | 影响 |
|---|---|---|---|---|
| P1 | 角色/武器在线刷新（`character/list`） | 有（`CharacterHistoryPage` 刷新/隔离验证） | **无**（UI-5 页只读；组合根未注册 `CollectCharacterWeapon`/`Validate*`/`ICharacterWeaponSource`） | 切换后 Windows 用户无法更新角色/武器 |
| P2 | 三模式挑战在线刷新（螺旋/剧诗/危战） | 有（`ChallengeHistoryPage` 刷新/隔离验证） | **无**（只读页；未注册 `Collect*`） | 同上 |
| P3 | 米游社/HoYoLAB 登录与角色选择 | 有（二维码/短信/手动 Cookie/密码 + Geetest + 设备指纹） | **仅手动 SToken/MID 粘贴**；无登录流程、无 LToken/CookieToken/设备指纹 | 切换后无法建立领域采集所需凭据 |
| P4 | Snap.Metadata 显示元数据 | 有（名称/图标/Snap 缓存 + 计算器回退） | **未注册 `IDomainMetadataService`/`SnapMetadataClient`**（领域页与 Chronicle 回退稳定 ID） | 显示质量回归 |
| P5 | 备份恢复 Scoped Replace | 有（三层范围/不可重叠/Tombstone/恢复点） | **仅 Merge**（`ArchiveTransferPage` 无 Replace） | 高级恢复能力缺失 |
| P6 | 恢复点回切 | 无（只读摘要，ADR 0023 不授权热回切） | 无 | 一致（ADR 0023） |
| P7 | 提瓦特小助手自动导入 | 有（需通行证角色） | 仅手动 UID+链接（自动入口明确不可用） | 小回归 |
| P8 | 窗口位置恢复/第二实例激活 | 无 | 无 | 两者都缺；切换时补 |
| P9 | 启动元数据网络依赖 | 有（离线冷启动可能停在“初始化失败”） | 无（启动不依赖网络） | WinUI 更优；切换后需保持 |

> **结论**：若要求“Windows 用户切换后功能不减少”，必须先补 P1–P5（工作量约等于一个新 UI 包，建议独立阶段，不要塞进 UI-6 切换包）。若接受“采集与高级恢复暂由旧 MAUI 宿主承担”，则切换包只需明确文档与入口提示，并在 §5-D1/D5 中记录过渡期。

### 1.7 证据与只读方法

- 本轮命令（只读）：`git status/log/show`、`Get-ChildItem`/`Get-Item`/`Measure-Object`（仅路径/大小/时间）、`Select-String`（仓库代码/文档）、`read`/`grep`、`git show`。
- 文件系统证据：`artifacts/ui6-recon/production-appdata-current.json`、`artifacts/ui6-recon/winui-appdata-current.json`（相对路径 + 大小 + mtime；**无内容、无绝对用户目录、无哈希**）。
- 既有证据：`artifacts/ui1/fixes-20261103/u10-dsh-retest/REPORT.md` 与 `logs/production-appdata-before.json`（2026-11-08 基线）；`UI_REDESIGN_UI0_IMPLEMENTATION.md:160`（明确未记录真实路径）。
- 框架证据：`Microsoft.Maui.Essentials 10.0.110`（Windows 目标 DLL）只读字符串扫描得到 `UnpackagedPreferencesImplementation`、`UnpackagedSecureStorageImplementation`、`DataProtectionProvider`、`ProtectAsync/UnprotectAsync`、`LOCAL=user`、`Settings`、`preferences.dat`、`#securestorage.dat`（用于确认实现机制，不涉及用户数据）。
- **未执行**：启动应用、打开生产 DB、读取/复制 `securestorage.dat`/`preferences.dat`、读取日志内容、运行 `dotnet build/test`、commit/push。

---

## 2. 切换方案选项

### 2.1 默认入口切换方式

| 选项 | 内容 | 优点 | 风险/成本 |
|---|---|---|---|
| A. 独立 WinUI exe 成为默认入口，旧 MAUI Windows 保留回退下载 | 改文档/下载页/CI 产物名；旧包保留一个周期 | 符合 ADR 0020 独立进程路线；WinUI 已具备大部分管理/数据能力 | 依赖 P1–P5 的功能决策；回退需旧包可下载 |
| B. 双产物并行过渡（默认仍 MAUI，WinUI 标“新版预览”） | 降低切换风险 | 用户需理解两套入口；数据只能一套（见 §2.2），过渡含义要写清 |
| C. 打包分发（MSIX/安装器等）后再切换 | 更像正式桌面应用 | 应用身份/Publisher 变化会改数据路径（§2.6）；签名/升级/商店规则需单独评审 |

**推荐（供评审）**：先 A（默认入口切换到独立 WinUI，旧包保留回退），打包方式另立选项（§5-D6）；若功能差距 P1–P5 未补，建议 B，并在文档中明确“采集继续使用旧版”。

### 2.2 生产库一次性迁移

| 选项 | 内容 | 优点 | 风险 |
|---|---|---|---|
| **A. 复制迁移（推荐）** | 关闭旧客户端 → 只读校验 → SQLite 一致性备份到新路径 → 校验 → 新宿主启用副本；源库保留（只读/改名标记） | 源库原样保留，可回退；失败不影响生产 | 需要额外磁盘（峰值 ≥ 源库×2 + 回退点 + 安全余量）；需明确“哪个是权威库” |
| B. 同路径接管 | 新宿主直接打开 `%LOCALAPPDATA%\User Name\...\Data\furinachronicle.db3` | 无复制、无额外空间 | 回退依赖备份；旧客户端若仍可启动会双写；标识/路径被绑定 |
| C. 逻辑 Archive 迁移 | 旧版导出 `.fca` → 新版导入 | 跨机器/跨平台可用；格式受控 | **会丢本机状态**：Undo/ChangeSet/OperationHistory/Receipt/Tombstone/Chronicle generation/UI 选择/恢复点等不迁移（ADR 0021:70-79）；不是“原样迁移” |

**推荐（供评审）**：A；B 作为空间受限或用户明确要求的备选；C 仅作为跨设备迁移的补充，不默认。

**A 的实现边界（不写代码，只列步骤与校验）**：
1. 要求用户关闭旧 MAUI Windows 实例（无法从新宿主可靠检测旧进程时，至少提示并检查锁文件/DB 可独占性）。
2. 只读打开生产库：校验 application id/Schema/`quick_check`；记录文件大小/mtime。
3. 用 SQLite 在线备份 API 复制到新路径（代码已有 `BackupAsync`/`Backup`：`SqliteArchiveExportSnapshotFactory.cs:53`、`ArchiveRestore.Recovery.cs:195`、`ArchiveRestore.Receipt.cs:375`）。**不得**在 WAL 活动时直接复制主文件（`OPEN_QUESTIONS.md:13`）。
4. 对副本再次只读校验：application id/Schema/`quick_check`、文件大小、关键表计数（档案/账号/抽卡/观察/挑战/手工/操作历史数量）与备份前一致。
5. 计算备份 SHA-256 并记录（仅本地、用户可见；不上传）；可选复制到用户选择的外部位置。
6. 新宿主首次以副本启动（只读模式先看数据）；确认后才允许写入；写前再次备份/建标记。
7. 迁移完成后：旧库保留（建议改名/只读或旧客户端打开时提示只读），并记录迁移 receipt（本地文件即可，含源/目标路径、大小、哈希、时间、版本）。

### 2.3 单写者与实例互斥

**现状（核实）**：
- `FurinaDatabase.AcquireInitializationLockAsync` 用 `<db>.initialize.lock`（`FileShare.None`）只在初始化期间持有，之后释放；文件保留（`FurinaDatabase.cs:177-198`）。
- `ArchiveRestoreWriterGate` 是 `static SemaphoreSlim`（进程内；`ArchiveRestore.Models.cs:11`），不能跨进程。
- 连接为 `FullMutex` + `SetBusyTimeout(5s)`（`FurinaDatabase.cs:57、80`）。
- 实测生产 `Data` 无 `-wal`/`-shm`（当前未打开），代码未显式设置 `journal_mode`。

**风险**：旧 MAUI 与新 WinUI 同时写同一库 → 逻辑冲突（Revision/Undo/Chronicle epoch）、SQLite busy/回滚、用户数据不可预期。

**建议组合（供评审）**：
1. **单实例**：WinUI 使用 `AppInstance`/激活重定向，保证新宿主只跑一个实例。
2. **数据目录级生命周期租约**：`<DataRoot>\session.lock`（`FileShare.None`）在进程存活期间持有；第二实例立即失败并说明原因（复用隔离宿主 `session.lock` 模式，`Ui1PreviewEnvironment.cs:137-139`）。
3. **旧客户端检测**：无法让旧 MAUI 遵守新锁，因此新宿主在首次迁移/非只读启动时：检测旧进程（同名进程/窗口）、尝试在写前取得临时独占锁、若失败则只读或拒绝写；文档要求用户关闭旧客户端。
4. **迁移完成后一致性**：旧库改名/标记（如 `<db>.migrated-to-winui` 旁注文件）并在旧客户端入口提示“数据已迁移，请使用新版”；或明确告知旧客户端可能看到旧数据（只读策略需决策）。

### 2.4 凭据迁移

**机制对比（已核实）**：

| 项 | MAUI Windows 生产 | WinUI 隔离宿主 |
|---|---|---|
| 文件 | `<root>\Settings\securestorage.dat`（4,051 B） | `<base>\UI4\WinUI\credentials\credentials.bin` |
| 保护 | `DataProtectionProvider`（descriptor 含 `LOCAL=user`；Essentials DLL 证据） | `ProtectedData`(CurrentUser) + entropy `FurinaChronicle.UI4.WinUI.Credentials.v1` |
| 内容 | 单 key `passport-accounts-v2`（`SecureStoragePassportAccountStore.cs:14`），JSON 数组：Id/Aid/Mid/DisplayName/LoginMethod/Realm/SToken/LToken/CookieToken/各时间戳/DeviceId/DeviceFingerprint/创建与更新时间（`:200-274`） | Payload v1：Id/Aid/Mid/SToken/DisplayName/DeviceId/时间戳/可用状态（`DpapiPassportCredentialStore.cs:365-379`）；读取时映射为 `ManualCookie` |
| 能力 | 三刷新、Teyvat 自动、领域采集（需 LToken） | 仅 SToken 抽卡刷新（`MiHoYoSTokenGachaUrlProvider`）；无 LToken/CookieToken/设备指纹 |

**选项**：
- **A. 用户重新授权（推荐）**：新宿主不读取旧凭据；旧凭据保留在旧宿主/旧文件；用户在新宿主“登录账号”手动添加 SToken/MID（或未来登录入口）。优点：无跨进程秘密迁移、无格式复刻风险；缺点：用户体验一次中断。
- **B. 显式一次性迁移（需维护者批准）**：在用户明确同意下，用 MAUI 兼容保护（DataProtectionProvider `LOCAL=user`）解密旧文件 → 只导入可安全映射字段 → 写入新 DPAPI 存储 → 标记旧文件已迁移（不删除或用户选择删除）。风险：需精确复刻 MAUI 文件/键格式（DLL 证据只到机制层，**格式未验证**）；必须审计、失败不恢复、不得日志；且即使迁移成功，WinUI 仍没有 LToken 消费方（P1/P2 未补时无收益）。
- **C. 不迁移（新宿主默认无凭据）**：等同于 A 的“跳过”路径；适合先切换、后登录。

**推荐（供评审）**：A（或 C），B 仅在“P1/P2 补齐且用户明确要求”时另立安全评审。

### 2.5 日志/诊断脱敏

- 现状：UI-4 设置页“诊断导出”只含版本/文件名/大小/安全声明（WinUI `WindowsSettingsPorts.cs:128-151`；MAUI `MauiSettingsPorts.cs:112-139`）。
- **现存风险**：MAUI 崩溃日志 `%LOCALAPPDATA%\FurinaChronicle\Logs\windows-unhandled.log` 记录异常全文（可能含路径/UID/记录信息），无脱敏、无保留上限（`Platforms/Windows/App.xaml.cs:26-47`）；WinUI 在启动 shell 失败时把异常全文写到 `%TEMP%\furina-ui4-buildshell-error.txt`（`FurinaChronicle.Windows/MainWindow.xaml.cs:100-110`），同样未脱敏、无清理策略。
- 要求（UI-6 实施时）：
  - 迁移/切换流程日志：不记录完整用户路径、UID、凭据、备份内容；哈希可记录但不上传；
  - 崩溃日志：默认本地、可关闭、有限保留、可一键清除；写入前脱敏（至少替换用户目录与凭据值），并在设置页显示位置；
  - 证据文件（截图/UIA/清单）放入 `artifacts/`（Git 忽略）并脱敏；
  - 任何日志/诊断不包含 `securestorage.dat` 内容。

### 2.6 应用标识/打包对数据路径的影响（关键）

- MAUI Windows 数据根由 `Package.appxmanifest` 的 `PublisherDisplayName` + `ApplicationId` 派生（本机实测 `%LOCALAPPDATA%\User Name\com.companyname.furinachronicle.app`；源文件 `Platforms/Windows/Package.appxmanifest:11-17`）。
- 因此：
  - **切换前冻结** `Publisher`/`PublisherDisplayName`/`Identity Name`/`ApplicationId`；
  - 若改为 MSIX/安装器并更换 Publisher/Identity：必须设计“旧路径探测 + 迁移到新路径”的兼容方案，否则用户看到空库（最坏误判为数据丢失）；
  - WinUI 自己的清单（`app.manifest`，无 MSIX 身份）目前不影响 MAUI 路径；一旦 WinUI 打包，需明确其身份与数据路径**故意与 MAUI 不同**时，如何迁移；
  - 旧 ApplicationId 数据（`com.companyname.furinaarchive.app`，20 KB DB）是否属于用户数据：需探测并提示（D8）。

---

## 3. 风险与回退

### 3.1 风险登记

| ID | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R1 | 新旧 Windows 同时写同一库 | 数据损坏/逻辑冲突/丢修订 | 单实例 + session lock + 旧进程检测 + 文档；迁移后旧库标记 |
| R2 | 迁移中断/半写 | 用户数据丢失 | 复制迁移 + 校验 + 源库保留 + 目标未校验不启用 |
| R3 | 备份不可用（WAL、磁盘不足、权限） | 无法回退 | 在线备份 API；峰值空间检查（源+目标+回退点+余量）；失败即中止 |
| R4 | 凭据迁移/导出泄露或失败 | 秘密泄露/用户需重新登录 | 推荐重新授权；B 方案需审计/端到端脱敏 |
| R5 | 未来 Schema/旧客户端回开 | 数据损坏/拒绝写入 | 不升 Schema；`FurinaDatabase` 兼容检查；回退边界文档化 |
| R6 | 路径发现错误（空库幻觉） | 用户误以为数据丢失 | 只读探测 + 不自动建库 + 明确“未找到”提示 + 手动选择路径 |
| R7 | 标识/打包变更导致路径迁移 | 同上 | 冻结标识（§2.6）或单独迁移方案 |
| R8 | 旧 ApplicationId/旧安装数据遗漏 | 用户旧数据不再可见 | 探测并提示（D8） |
| R9 | 功能回归 P1–P5 | 用户无法采集/高级恢复 | 先补功能或明确接受回归并保留旧宿主 |
| R10 | 日志含敏感信息 | 隐私风险 | 脱敏/保留/清除策略（§2.5） |
| R11 | 未授权真实凭据/网络验收 | 成功路径未验证 | 迁移演练用合成副本；SToken 成功刷新需用户授权环境单独验收 |

### 3.2 回退设计

- **回退定义**：关闭新宿主 → 用迁移前一致备份/保留的旧库恢复 → 旧 MAUI 能正常打开并读写。
- 条件：迁移前后 **不升 Schema、不改公开格式、不改身份语义**；否则回退不成立（ADR 0020:65-67、`OPEN_QUESTIONS.md:13`）。
- 边界：若用户已在新区写入，回退会丢失“新区新增数据”——迁移向导必须明确“回退=回到迁移点”；建议迁移后立即做一次“旧库只读/新库写入”的标记与提示。
- 保证：源库在整个迁移过程中不被修改（复制而非移动）；目标校验失败不切换；回退脚本/操作步骤写入文档并由用户确认。

### 3.3 用户数据不可丢保证

1. 源库保持原样（只读校验 + 复制）；任何失败均不删除源。
2. 迁移前强制一致备份（应用私有 + 可选外部位置），并记录大小/SHA-256/application id/Schema/时间。
3. 目标库通过只读校验（`quick_check` + application id + Schema + 关键表计数）后才允许启动写入。
4. 迁移记录本地 receipt（源/目标路径、大小、哈希、时间、版本、操作者确认），可重启后查询。
5. 不迁移“可重建缓存/预览库/旧凭据”；凭据按 §2.4 处理。
6. 旧宿主保留可回退一个发布周期（D5）。

### 3.4 迁移验收清单（建议）

| 阶段 | 检查 | 通过标准 | 证据 |
|---|---|---|---|
| 迁移前 | 用户关闭旧客户端；磁盘余量 | ≥ 源库×3 + 固定余量 | 日志/截图 |
| 迁移前 | 只读校验源库 | application id/Schema 匹配；`quick_check` ok | 本地报告（无内容） |
| 备份 | SQLite 一致备份 | 副本可打开、大小/哈希记录、`quick_check` ok | 哈希/大小 |
| 复制 | 目标副本 | 只读校验同上；关键表计数一致 | 计数对比 |
| 首启 | 新宿主只读打开 | 档案/账号/抽卡/观察/挑战数量可见 | 截图/UIA |
| 写入 | 小范围写入（如新建档案或一次刷新） | 提交成功；Revision/Undo/Chronicle 行为符合预期 | 集成测试/UIA |
| 回退演练 | 恢复备份 → 旧客户端打开 | 数据完整、可读写 | 手工会话记录 |
| 双端 | Archive/Portable 导出→导入 | 语义往返通过 | 既有验证工具 |
| 收尾 | 旧库标记/只读/提示 | 用户明确知道哪个库是权威 | 文档/截图 |

**明确不做**：不在未备份/未校验时写入生产库；不把 SQLite 文件直接作为公开备份；不在 WAL 活动时复制主文件；不在迁移中改 Schema/格式。

---

## 4. 建议实施顺序与工作量（不写代码）

| 阶段 | 内容 | 依赖 | 粗估 | 备注 |
|---|---|---|---|---|
| P0 | 本评审 + 维护者决策（§5） | — | — | **当前阻塞点** |
| P1 / UI-6a | 只读探测与迁移工具：路径发现、只读校验、在线备份、清单对比、dry-run 报告 | P0 的 D3/D9 | S–M | 不改生产；可独立并行 |
| P2 / UI-6b | 生产组合根与环境抽象：把 `Ui1PreviewEnvironment` 替换/泛化为可配置环境（生产路径、无种子、正式元数据/图标缓存路径、恢复点目录） | P1；D3 | M–L | 需避免再复制一套组合根 |
| P3 / UI-6c | 单实例 + session lock + 旧进程检测 + 迁移向导（检测→说明→备份→复制→校验→切换→回退入口） | P1/P2；D2/D9 | M–L | 必须隔离环境先验收 |
| P4 / UI-6d | 功能补齐或回归接受（P1–P5：领域采集/登录/元数据/Replace） | P0 决策；工作量可能超过 UI-6 本身 | L | 建议独立阶段（可命名 UI-7） |
| P5 / UI-6e | 默认入口切换：CI 产物命名、下载页/README、旧包回退页、打包（D6）、文件关联/快捷方式 | P3（+P4 若要求无回归） | M | 需发布/分发评审 |
| P6 / UI-6f | 收口：性能/无障碍/字体/缩放矩阵、双端验收、日志脱敏、文档与证据 | P5 | M–L | 计划 §17 明确“未测量” |

**并行边界（建议）**：P1 与 P4 可并行（不同目录/不同人）；P2 必须在 P3 之前且由一人负责组合根；P5 只在 P3 验收后做；CI/文档改动只在 P5 触碰。UI-6 不应与 UI-4/5 的页面改动混在同一批文件。

---

## 5. 待维护者决策问题清单（不替用户决定）

> 每项给出选项与建议，但最终由维护者/用户确认；未确认前实现不得开工。

**D1 默认入口切换形态**
- A. `FurinaChronicle.Windows` 直接成为默认下载/安装入口，旧 MAUI Windows 保留一个发布周期作回退（推荐，若 P1–P5 已接受/补齐）。
- B. 双产物并行过渡（默认仍 MAUI，WinUI 标“新版预览”）。
- C. 先打包（MSIX/安装器）再一次性切换。
- 影响：回退能力、文档、CI 产物名、用户迁移节奏。

**D2 迁移触发方式**
- A. 新宿主首次启动检测旧库并引导“导入现有数据”（推荐，可跳过）。
- B. 设置页手动“从旧版迁移”。
- C. 便携/命令行参数（高级用户/支持场景）。
- 建议 A+B（首启引导 + 事后可再触发）；C 仅作调试。

**D3 生产库接管方式**
- A. 复制迁移（源库保留；推荐）。
- B. 同路径接管（需更强备份/回退；空间受限备选）。
- C. 逻辑 Archive 迁移（会丢本机 Undo/Receipt/Tombstone/Chronicle 状态；不推荐默认）。
- 影响：磁盘空间、回退语义、旧客户端可见性。

**D4 凭据策略**
- A. 要求用户重新授权（推荐；新宿主不读旧凭据）。
- B. 显式一次性迁移（需安全评审；需复刻 MAUI 文件格式，未验证）。
- C. 不迁移（新宿主从无凭据开始）。
- 影响：用户体验、秘密安全、P1/P2 可用性（需 LToken）。

**D5 旧宿主保留期与可见性**
- A. 永久保留（作为只读/导出工具）。
- B. 保留 1–2 个发布周期后停止分发（推荐）。
- C. 立即停止分发但保留回退下载页。
- 影响：回退窗口、支持成本、双写风险。

**D6 打包与分发**
- A. 继续 unpackaged self-contained ZIP（现状；最快）。
- B. MSIX（需身份/Publisher/升级/数据路径方案）。
- C. 安装器（快捷方式/文件关联/卸载器）。
- 建议 A 用于切换首版、C 作为后续正式分发；B 需先解决 §2.6。

**D7 生产库备份策略**
- A. 迁移强制一次一致备份（应用私有 + 可选外部；推荐）。
- B. 每次启动自动备份并保留 N 份（后续可选）。
- C. 仅依赖用户手动导出 `.fca`。
- 注意：`.fca` 是逻辑导出，不含本机 Undo/Receipt/Tombstone（ADR 0021:70-79），不能替代迁移备份。

**D8 旧 ApplicationId 数据（`com.companyname.furinaarchive.app`）**
- A. 忽略。
- B. 探测到就提示用户“发现旧版本数据，可导出/迁移”（推荐）。
- C. 自动迁移（不推荐无评审自动执行）。

**D9 双写保护强度**
- A. 新宿主启动检测旧进程/锁并拒绝进入写模式（推荐）。
- B. 只提示、允许继续（不推荐）。
- C. 仅单实例 + 文档要求（不足以防旧 MAUI 双写）。
- 建议 A+C。

**D10 Schema/格式冻结**
- A. UI-6 不升 Schema（当前 12）、不改 Portable/Archive 格式与身份/Undo/Chronicle 语义（推荐/必须）。
- B. 若确需变更：另立阶段，先 ADR 再实施。

**D11 应用标识/打包身份冻结**
- A. 切换前冻结 `Publisher`/`PublisherDisplayName`/`ApplicationId`（推荐）。
- B. 允许变更但必须先做“旧路径→新路径”迁移方案与演练。

**D12 功能差距处理**
- A. 先补 P1–P5 再切换（功能不回退；工期最长）。
- B. 接受回归：WinUI 只做管理/查看/抽卡，领域采集与 Replace 继续用旧 MAUI；文档明确（推荐若尽快切换）。
- C. 分阶段：切换后按 P1→P5 顺序补（需明确的过渡期说明）。

---

## 6. 未验证事项与已知不一致

**未验证（本轮明确未做）**
1. 生产库的实际 `PRAGMA application_id/user_version/quick_check`（只按代码基线推断为 0x46554348 / 12）。
2. 生产 `securestorage.dat` 的具体条目编码/键格式（只确认实现机制为 DataProtectionProvider `LOCAL=user`）。
3. 旧 ApplicationId 数据（`furinaarchive.db3`）的 Schema/内容/归属。
4. 生产库当前是否含 UI-1 预览库以外的一致性（未读内容）。
5. MAUI Windows `FileSystem.AppDataDirectory` 的精确框架公式（实测布局已验证；`User Name` = manifest PublisherDisplayName，源文件可证）。
6. 真机/在线成功路径：SToken 成功刷新、Teyvat 自动导入、领域采集（需真实凭据/网络授权）。
7. 迁移在不同 Windows 版本/非中文用户名/漫游配置下的路径行为。
8. 性能/无障碍数值（计划 §17 明示未测量）。

**已知不一致/风险记录**
1. MSIX/安装器若改变 Publisher/Identity，导致 MAUI 数据路径变化（§2.6）；必须先迁移。
2. MAUI 崩溃日志无脱敏/保留上限（§2.5），切换前建议修。
3. WinUI 未接 Snap.Metadata（P4），领域页/Chronicle 显示回退 ID；切换后显示质量下降。
4. WinUI 无领域采集（P1/P2）与登录（P3）、无 Replace（P5）；切换前需决策（D12）。
5. WinUI 隔离宿主默认根 `%LOCALAPPDATA%\FurinaChronicle` 目前仅有一条 MAUI 崩溃日志（`Logs\windows-unhandled.log`），无 UI4 数据；迁移/切换时不要误判为 WinUI 既有数据。
6. 生产 `Data\UI1\Maui\ui1-preview.db3` 是 UI-1 预览库（非业务数据），迁移时应排除并提示清理。
7. 生产库最后写入 2026-11-12T02:34:21Z，说明 Phase 9/10 期间的在线验收可能写入了真实用户库；迁移必须重新采样/备份，不能沿用 11-08 基线。

---

## 7. 证据与复现

### 7.1 只读命令（可复现，不触内容）

```powershell
# 目录/大小/时间（不读取内容）
$root = Join-Path $env:LOCALAPPDATA 'User Name\com.companyname.furinachronicle.app'
Get-ChildItem $root -Recurse -File | Measure-Object Length -Sum
Get-Item (Join-Path $root 'Data\furinachronicle.db3') | Select-Object Length, LastWriteTimeUtc

# 生产标识（源清单）
Get-Content 'FurinaChronicle.App/Platforms/Windows/Package.appxmanifest'

# 隔离宿主路径定义
Get-Content 'FurinaChronicle.Windows/Composition/WindowsPaths.cs'

# 应用崩溃日志路径（代码）
Get-Content 'FurinaChronicle.App/Platforms/Windows/App.xaml.cs'
```

> 迁移实现阶段建议补充（需用户授权、只读）：SQLite `ReadOnly` 打开 + `PRAGMA application_id/user_version/quick_check`；在线备份后对副本做同样校验；**不要**在探测阶段创建目录或写库。

### 7.2 本轮产物（Git 忽略）

- `artifacts/ui6-recon/production-appdata-current.json`：生产 app-data 相对路径 + 大小 + mtime（无内容/无绝对用户目录/无哈希）。
- `artifacts/ui6-recon/winui-appdata-current.json`：WinUI 默认根现状（仅日志文件）。
- 本文件：`docs/project/UI6_PRODUCTION_SWITCH_REVIEW.md`（Lead 提交；不包含秘密）。

### 7.3 关键文件/行号索引

**路径与存储**
- `FurinaChronicle.App/Composition/PersistenceServiceCollectionExtensions.cs:61-63`（DB 路径）、`:24-65`（路径变量）
- `FurinaChronicle.App/Persistence/PlatformStoragePaths.cs:10-29`（AppData/Cache）
- `FurinaChronicle.App/Composition/GachaServiceCollectionExtensions.cs:79、97、104、108、120-130`（元数据/图标/横幅/Portable）
- `FurinaChronicle.App/Composition/ArchiveServiceCollectionExtensions.cs:23-30`（导出/恢复会话）
- `FurinaChronicle.App/Composition/PresentationServiceCollectionExtensions.cs:70-72`（SnapMetadata 缓存）
- `FurinaChronicle.App/Persistence/SecureStoragePassportAccountStore.cs:14-15、200-274`（凭据键/字段）
- `FurinaChronicle.App/Persistence/PreferencesArchiveSelectionStore.cs:13-15`、`PreferencesPassportSelectionStore.cs:12`、`UI/Gacha/GachaAppearanceSettings.cs:9`、`Composition/MauiSettingsPorts.cs:18`（Preferences 键）
- `FurinaChronicle.App/Platforms/Windows/Package.appxmanifest:11-17`（PublisherDisplayName/Identity）
- `FurinaChronicle.App/Platforms/Windows/App.xaml.cs:26-47`（崩溃日志）
- `FurinaChronicle.Infrastructure/Persistence/Sqlite/FurinaDatabase.cs:12、34、57、75-198`（Schema/app id/全互斥/初始化锁）
- `FurinaChronicle.Infrastructure/Persistence/Sqlite/SqliteGachaAtomicChangeStore.ArchiveRestore.Recovery.cs:95-115`（恢复点目录）
- `FurinaChronicle.Infrastructure/Archives/Portable/SqliteArchiveExportSnapshotFactory.cs:53`、`ArchiveRestore.Receipt.cs:375`（SQLite 在线备份）
- `FurinaChronicle.Infrastructure/Diagnostics/Ui1PreviewEnvironment.cs:27-118、131-140、227-247`（隔离环境/租约/占位）
- `FurinaChronicle.Windows/Composition/WindowsPaths.cs:6-49`、`WindowsComposition.cs:31-39、63-72、134-149、151-181`、`WindowsSettingsPorts.cs:13-18、128-151`、`DpapiPassportCredentialStore.cs:13-22、282-329`、`WindowsStartupProbe.cs:10-40`、`EnvironmentHolder.cs`、`STokenRefreshComposition.cs`
- `FurinaChronicle.Windows/FurinaChronicle.Windows.csproj:9-28`、`MainWindow.xaml:26-48`、`MainWindow.xaml.cs`（标题/导航/固定尺寸）

**决策/规范**
- `docs/decisions/0020-platform-specific-ui-hosts.md:31-42、53-59、63-70`
- `docs/decisions/0021-logical-archive-v1-fidelity.md:70-79`（本机状态不迁移）
- `docs/decisions/0022-archive-merge-receipt-and-settings.md:14-26`
- `docs/decisions/0023-scoped-archive-replace-and-recovery-point.md:20-33、61-99、130-145`
- `docs/project/OPEN_QUESTIONS.md:13`（不降级、禁止 WAL 复制）
- `docs/development/VERSIONING.md:98-105、135-145`
- `docs/project/UI_REDESIGN_UI4_IMPLEMENTATION.md:151-156`、`UI_REDESIGN_UI5_IMPLEMENTATION.md:91-96`
- `docs/decisions/DECISION_REGISTER.md:41-44`（D34–D37）

**桌面/发布**
- 外层 UI 计划（工作区外）`FurinaChronicle_UI_Redesign_Plan_20261021.md:180-190、808-814`（迁移门槛/旧前端退出）
- `.github/workflows/build-and-test.yml:60-131`（Windows/win-preview 产物）
- `docs/development/CONTINUOUS_INTEGRATION.md`、`ANDROID_PHYSICAL_DEVICE_DEPLOYMENT.md`

---

## 8. 附：迁移期间路径速查（供实现阶段核对，非固定值）

| 用途 | MAUI 生产（源） | WinUI 目标（建议） | 处理 |
|---|---|---|---|
| 业务库 | `<root>\Data\furinachronicle.db3` | 复制迁移；源保留 | 备份→复制→校验 |
| 恢复点 | `<root>\Data\archive-recovery\*`（当前无） | 同库同目录语义 | 随库迁移；无则跳过 |
| 元数据库/事件目录 | `<root>\Data\genshin-metadata.db3`、`Data\metadata\...` | 复制或重建（缓存类） | 可选 |
| 图标/横幅缓存 | `<root>\Cache\gacha-*\` | 可重建 | 不迁移 |
| Portable/Archive 临时 | `<root>\Cache\gacha-portable-*`、`archive-export-snapshots`、`archive-restore-sessions` | 新宿主私有临时目录 | 不迁移/清理 |
| 选择状态 | `<root>\Settings\preferences.dat`（键见 §1.2） | WinUI 设置 JSON | 可重建；便携设置需映射（D7 之外另议） |
| 凭据 | `<root>\Settings\securestorage.dat` | `UI4/WinUI/credentials/credentials.bin` | **重新授权**（D4） |
| 崩溃日志 | `%LOCALAPPDATA%\FurinaChronicle\Logs\windows-unhandled.log` | 统一策略后位置 | 清理/脱敏 |

---

*本文是只读预研与评审材料，不构成生产切换、数据迁移或默认入口变更的授权；所有路径/大小/时间均为 2026-11-13 04:5x 本机只读实测，未读取任何数据库、凭据或日志内容。*

## 9. 维护者决策记录（2026-11-13）

以下决策由维护者在评审时明确选择（均为推荐方案 A）：

| 编号 | 决策 | 选择 |
|---|---|---|
| D1 默认入口形态 | 独立 WinUI 立即成为默认，旧 MAUI Windows 保留一个发布周期回退 | **A** |
| D2 迁移触发 | 首次启动 WinUI 时引导用户确认迁移 | **A** |
| D3 库接管 | 复制迁移（只读校验→在线备份→副本校验→新宿主启用；源库保留） | **A** |
| D4 凭据 | 要求用户在新宿主重新授权（不复制旧凭据文件） | **A** |
| D5 旧宿主保留 | 保留一个发布周期，之后移除 | **A** |
| D6 打包分发 | 继续 self-contained unpackaged 发布，文档/脚本分发 | **A** |
| D7 备份策略 | 迁移前强制一次备份并校验，之后用户手动备份 | **A** |
| D12 功能差距 | 接受当前差距（领域在线采集/登录/Snap 元数据）并保留旧宿主，后续阶段补齐 | **B** |

实施约束（由上述决策推导）：
- 不改变应用标识/PublisherDisplayName/ApplicationId；保持 unpackaged 路径稳定。
- 生产库只做“复制迁移 + 校验 + 回退”，不删除源库；迁移前强制备份。
- 凭据只做“重新授权”路径，不实现旧 SecureStorage 解密的自动迁移；如需显式迁移须另立安全评审。
- 单实例/数据目录 session.lock/旧进程检测是切换前置条件（必须先实现并通过测试）。
- 切换前必须完成：迁移向导（首启引导、可回退）、日志脱敏、验收清单演练；真实凭据/网络成功路径
  仍需授权环境验证。
- 旧宿主保留期：至少一个发布周期；移除需再次确认。


实现状态见 [UI-6 双端收口实施记录](UI6_IMPLEMENTATION.md)：打包/CI 默认入口、单实例、迁移向导前置与日志/无障碍已交付；生产组合根与真实数据演练为剩余发布步骤。
## 10. 后续修订（2026-11-13，用户指示，覆盖 D3/D5）

- **D3 修订**：不再采用“复制迁移到新宿主目录”。WinUI 宿主**直接使用与 MAUI Windows 宿主相同的
  生产数据库文件**（同一路径、就地读写）；首次使用现有库时做只读校验、强制备份并写接管标记，
  失败不修改原库。`FURINA_UI1_DIRECTORY` 保留为显式隔离/测试模式。
- **D5 修订**：旧 MAUI Windows 宿主**长期保留**，用于兼容性、基本功能查看与验证；它不再进入 CI
  构建产物，也没有专门的 UI 设计优化。两个宿主不得同时运行。
- **CI 修订**：Windows job 只构建 WinUI 3 宿主，已移除 MAUI Windows x64 编译；Android job 保持
  MAUI Android 构建。
- **单写者**：WinUI 使用数据根 `session.lock` 并检测旧 MAUI 进程；旧宿主不识别该锁，因此需要用户
  先关闭其中一个。真正的并发双写保护仍需后续跨宿主协调（当前以进程检测与文档约束为准）。
- 其余决策（D1/D2/D4/D6/D7/D12）不变。
