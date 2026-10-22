# Phase 8B.7 质量门槛与 8C 交接

状态：Accepted 计划的实施记录

实施状态：In progress；工程工作进行中，设备／文件证据尚未关闭

基线：`feat/phase-8 @ abada0af5b9287dc8d045d6cd32ba0962244e7f5`

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
| Git | `feat/phase-8 @ abada0a`；开始时工作区干净 |
| 数据库 | 当前 Schema 7；需要补直接 populated 6→7 迁移证据 |
| 应用版本 | `0.8.7 / 8B.6`；本包完成工程入口后推进为 `0.8.8 / 8B.7` |
| 自动化 | Release 517／517 通过 |
| .NET | 仓库 SDK 选择仍由 `global.json`／构建确定；主机同时安装 .NET 10 与 11 RC |
| Android | `E:\AndroidSDK` 可用；发现 `emulator-5554`，未发现物理设备 |
| Windows | Visual Studio 正在运行；执行真实 App／旧库升级前必须避免文件占用并只操作一致副本 |
| UI 重构 | UI-0 已提交；8B.7 继续使用现有 MAUI 生产服务取得跨端证据，不把 UI-0 或未来 UI-5 当成阶段完成 |

## 工作包

| 工作包 | 交付 | 当前状态 |
|---|---|---|
| P0 | 基线、边界和证据矩阵 | In progress |
| P1 | 正式 exporter 生成的虚构验收样本与严格语义比较 | Pending |
| P2 | 直接 Schema 6→7、后段失败和独立领域出口安全回归 | Pending |
| P3 | Windows／Android 可达且可关闭的隔离验收入口 | Pending |
| P4 | 集成回归、版本和双平台设备用构建 | Pending |
| P5 | Windows 实际旧库升级、Android 实际新库创建 | Awaiting device / old database evidence |
| P6 | 两个 Dataset 的 Windows↔Android 双向文件往返 | Awaiting device evidence |
| P7 | Phase 8B 退出评审和 8C 输入 | Pending |

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
