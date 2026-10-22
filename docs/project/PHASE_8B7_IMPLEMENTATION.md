# Phase 8B.7 质量门槛与 8C 交接

状态：Accepted 计划的实施记录

实施状态：Implementation complete；Android physical startup verified；Awaiting remaining device/file verification

实施基线：`feat/phase-8 @ abada0af5b9287dc8d045d6cd32ba0962244e7f5`

最近真机启动证据：`feat/phase-8 @ 174fa9574440e3322f8e552214fbcf3112a3f320`

## 目标与边界

8B.7 汇总 8B.0～8B.6 已建立的身份、时间、来源、修订、领域 Portable 与 Chronicle 能力，补齐质量门槛和真实跨平台证据，并形成 8C 的可靠输入。

本包不新增完整 Archive、Sync／ChangeJournal、在线角色或挑战 Provider、全部挑战模式、完整 Chronicle 产品页面或 UI 视觉重构。角色／武器及挑战 Portable v1 在真实双向文件往返关闭前继续是 `internal / unpublished`；互通通过本身也不自动将格式公开发布。

实施状态分为：

- `Implementation complete`：入口、样本、比较、自动化和可运行产物完成；
- `Device/file verification complete`：真实应用数据库与跨端文件证据完成；
- `Phase 8B closed`：全部硬性门槛、文档和 8C 交接一致。

设备或历史库暂不可用时继续完成工程工作，但阶段保持 `Awaiting device evidence`，不得以测试、构建或模拟器冒充物理真机和真实文件验收。

## 当前基线

| 项目 | 2026-10-22 实际结果 |
|---|---|
| Git | 实施基线 `abada0a`；Android 真机启动证据固定于 `174fa95`，工作区干净 |
| 数据库 | 当前 Schema 7；直接 populated 6→7、后段失败回滚和取消已有自动化证据 |
| 应用版本 | 真机产物 `0.8.8.169 / 8B.7`；Build 继续由 Git commit count 生成 |
| 自动化 | Release 525／525 通过（设备验收前工程基线） |
| .NET | 仓库 SDK 选择仍由 `global.json`／构建确定；主机同时安装 .NET 10 与 11 RC |
| Android | `E:\AndroidSDK` 可用；ARM64 物理设备已完成覆盖安装和冷启动，设备标识未写入文档；隔离数据库和跨端文件验收仍待完成 |
| Windows | Visual Studio 正在运行；执行真实 App／旧库升级前必须避免文件占用并只操作一致副本 |
| UI 重构 | UI-0 已提交；8B.7 继续使用现有 MAUI 生产服务取得跨端证据，不把 UI-0 或未来 UI-5 当成阶段完成 |

## 工作包

| 工作包 | 交付 | 当前状态 |
|---|---|---|
| P0 | 基线、边界和证据矩阵 | Completed |
| P1 | 正式 exporter 生成的虚构验收样本与严格语义比较 | Automated complete |
| P2 | 直接 Schema 6→7、后段失败和独立领域出口安全回归 | Automated complete |
| P3 | Windows／Android 可达且可关闭的隔离验收入口 | Android physical startup verified；remaining workflow evidence pending |
| P4 | 集成回归、版本和双平台设备用构建 | Automated/build complete；fixed-commit ARM64 artifact installed and launched |
| P5 | Windows 实际旧库升级、Android 实际新库创建 | Awaiting device / old database evidence |
| P6 | 两个 Dataset 的 Windows↔Android 双向文件往返 | Awaiting device evidence |
| P7 | Phase 8B 退出评审和 8C 输入 | Draft complete；cannot close before P5/P6 |

## 已完成工程证据

- `Phase8B7MigrationTests` 以生产写入形成 populated Schema 6 边界，覆盖 Gacha、角色／武器、挑战、修订、Undo、Tombstone、Receipt，并验证 6→7 保留、外键、表、触发器及后段冲突回滚；预取消保持库不变。
- `Phase8BValidationMode=true` 构建只显示隔离验收页，使用 `phase8b7-validation` 数据目录，不执行元数据联网刷新或 Passport 凭据维护。默认构建路径和数据库不变。
- 页面通过实际系统 picker 的内容流调用正式 Reader／Plan，确认后调用正式原子 Apply；支持两个领域的 Portable 和 Markdown 导出。Android 不把 SAF URI 当路径或写入诊断。
- `eng/phase8b` 工具从正式领域 Store 和 exporter 生成两类合成成功包；另生成坏指纹、合法指纹 future major、截断、超限和同引用不同内容输入。正式 Reader 已分别接受或按预期拒绝。
- 严格比较只忽略目标本地档案／账号 ID、包指纹、导出时间和接收端操作记录；保留稳定事实 ID、账号归属、原时间偏移、来源、完整度与手工修订。变异测试证明时间、来源、修订或账号归属损失均会失败。
- 两个 Dataset 均已通过宿主 Source→Domain→SQLite→Export→Read／Plan→Apply→Re-export 严格语义比较及重复导入 NoOp。该证据不冒充真实设备文件往返。
- Windows 旧库准备使用工具的 SQLite Online Backup，不只复制主文件；验收构建可通过 `FURINA_PHASE8B7_DIRECTORY` 选择绝对隔离目录，且不会覆盖已存在的目标副本。

## Android 真机启动证据（2026-10-22）

- 源码为 clean `174fa9574440e3322f8e552214fbcf3112a3f320`，应用版本为 `0.8.8.169 / 8B.7`，目标 RID 为 `android-arm64`。
- 使用指定 RID 的定向 clean 与 Release build 生成 `FurinaChronicle-0.8.8-8B.7-validation-arm64-Signed.apk`；大小为 21,518,210 字节，SHA-256 为 `d592bf15119e2112653c7ed090440ce9068056fa264aefc24d6ee85a39e769e7`。
- 通过 `adb install -r` 覆盖安装，设备上的首次安装时间保持不变，未执行 uninstall 或 clear。冷启动完成后进程持续存在，MainActivity 位于前台，等待 10 秒后未发现 AndroidRuntime／.NET 启动崩溃。
- 同一源码的旧通用 APK 曾因陈旧 `android-arm64` 中间资源在 MAUI 初始化时抛出 `MissingMethodException`。指定 `android-arm64` 定向清理和重新构建后恢复正常；通用规则已写入 [Android 真机构建与部署](../development/ANDROID_PHYSICAL_DEVICE_DEPLOYMENT.md)。
- 本证据只关闭 ARM64 产物的安装和启动子项，不证明 Android 隔离数据库 Schema 7 的新建／重开，也不证明角色／武器和挑战 Portable 文件往返；P5／P6 因此仍保持未关闭。

相关本地提交：`ce2ec5d`、`db1a4b5`、`a50dd4f`、`ec4ce97`、`7ae1039`、`895c4e6`。

## 验收入口约束

- 通过显式构建开关启用，不替换默认导航或默认数据库；验收构建使用独立、明确标记的应用数据子目录。
- 文件读取使用系统 picker 返回的流；Android SAF URI 不作为普通路径，也不写入 Portable。
- Preview、账号／档案映射和冲突在 Apply 前显示；确认后调用正式 Reader／Plan／原子 Apply，不直接 SQL 写业务表。
- 导出通过正式 exporter 生成并交给平台 saver；同时提供 Markdown，以便人工核对。
- 入口显示版本、Phase、commit、平台、数据库标签和 Schema；诊断结果不得包含凭据、真实业务原文或设备 URI。
- 取消、拒绝和失败不得留下半份父实体、事实或 Receipt；重复导入必须显示 Skip／NoOp，而不是产生重复历史。

## 门槛矩阵

| 编号 | 门槛 | 自动化／工程 | 实际平台 |
|---|---|---|---|
| M01 | 新库、v1～v6→7、外键和触发器 | 必须 | Windows 旧库、Android 新库 |
| M02 | future Schema、错误 application ID、迁移后段失败不变 | 必须 | 故障注入即可，不冒充设备实测 |
| I01 | 隔离验收入口调用正式服务 | 必须 | Windows／Android 实际运行 |
| P01 | 角色／武器 Portable 双向语义往返 | 必须 | 必须，含来源修订和时间偏移 |
| P02 | 挑战 Portable 双向语义往返 | 必须 | 必须，含观察历史和未知发生时间 |
| P03 | 幂等、已有目标、坏包、预览过期和取消 | 必须 | 两端至少走成功、重复和拒绝／取消 |
| H01 | 跨域 Undo、purge、容量确认 | 复用并补实际缺口 | 触及页面时才要求新增交互证据 |
| C01 | generation、游标、重建和离线本地元数据 | 复用并回归 | 不要求完整 Chronicle UI |
| E01 | 无 Chronicle 时领域 Query／History／Markdown／Portable | 必须 | 入口复用服务，不另造页面事实源 |
| G01 | UIGF、刷新、统计和 Gacha Portable 不退化 | 全量适用回归 | 生产路径受影响时补双端走查 |
| Q01 | 固定源码、版本、版权、diff、双平台构建 | 必须 | 每份设备证据绑定具体产物 |
| D01 | 支持矩阵、退出评审和 8C 输入一致 | 必须 | 如实引用实际结果 |

## 证据纪律

每项结果分别标记 `Automated`、`Build verified`、`Emulator`、`Physical device`、`Maintainer reported`、`Not run`、`Blocked` 或 `Deferred`。证据至少记录源码提交、dirty 状态、应用版本、平台、Schema 前后、样本类型、文件大小与 SHA-256、语义比较、耗时测量方法和限制。

只提交虚构样本、脚本、期望值和脱敏汇总。真实数据库、Hutao 原始 JSON、UID、Cookie、SToken、AuthKey、验证码、设备 URI 和私人业务明细不得进入 Git、日志或报告。

## 已知人工门槛

工程入口和产物完成后，维护者仍需：

1. 在 Windows 用真实旧版生成或历史一致备份的 Schema 6 数据库副本，经实际应用路径升级、退出、重开并核对；不得只改 `user_version` 伪造旧库。
2. 在 Android 独立验收数据库中由应用创建 Schema 7，导入样本、重开并导出；不得卸载或清除现有用户数据。
3. 对角色／武器和挑战分别完成 Windows→Android→Windows，以及 Android 发起→Windows 的文件往返、重复导入和至少一次取消／拒绝路径。

在这些证据完成前，8B.7 可以达到 `Implementation complete`，但 Phase 8B 不标 Closed。
