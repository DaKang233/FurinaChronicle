# Phase 8B.4 实施契约与验证记录

状态：Accepted implementation contract

实施状态：In progress（2026-10-08）

基线：`feat/phase-8`，起始提交 `184f3cd97a421e72a41009d408e5007250bf7a28`

## 本轮边界

8B.4 将现有 Gacha 产品入口接到 ADR 0013 的原子 ChangeSet／Revision／Undo／
Tombstone／Portable Apply 基础。Q1～Q7 的首版产品选择由
[ADR 0014](../decisions/0014-phase-8b4-gacha-production-semantics.md) 固定。

本轮不建设通用 EventStore、Sync／ChangeJournal、完整 Archive、Chronicle 基础，
也不提前统一后续角色、武器或挑战领域。

## P0 基线事实

- 分支／HEAD：`feat/phase-8 @ 184f3cd97a421e72a41009d408e5007250bf7a28`。
- 开始时工作区干净，无维护者未提交修改。
- Release 单元测试基线：421／421 通过。
- 数据库：Schema 5；ADR 0013、8B.3 实施契约和 Portable v1 Accepted 文档有效。
- 页面：账号／档案管理位于 `ArchivePage`；Gacha 与管理页选择状态相互独立。

## 生产入口清单

| 入口 | 当前调用路径 | P0 结论／8B.4 目标 |
|---|---|---|
| UIGF 与小助手两模式 | `GachaPageViewModel` → `ImportUigfGachaRecords` → `SaveBatchAsync(PreserveExisting)` | 迁移到统一 Gacha 政策和原子提交；补空写 Revision，非空差异冲突 |
| SToken 刷新 | `GachaPageViewModel` → `RefreshGachaRecords` → `SaveBatchAsync` | 迁移到统一刷新政策；Tombstone 过滤并报告 |
| 手工 URL 刷新 | 同上，仅 URL 来源不同 | 官方响应仍为 `OfficialApi`，不是 `UserEntered` |
| Windows Web 缓存 | 缓存只提供 URL，随后同一官方分页 | 仅 Windows 可用；保存语义与其他刷新一致 |
| 小助手下载 | 下载 UIGF 后进入 UIGF 核心 | 显式导入，不是自动采集或重引入授权 |
| 兼容 JSON | `ImportGachaRecords` 已注册 DI，无当前页面调用 | 移出生产 DI 或接统一政策；不得保留可注入旁路 |
| Portable Apply | `IGachaPortableImportApplier` → `SqliteGachaAtomicChangeStore.ApplyAsync` | 已有 AddOnly 原子入口；补产品文件流程、容量和 Tombstone 预览 |
| 记录纠正／普通删除／Undo | 仅有内部原子端口和测试，无完整产品 UI | 增加 Gacha 专用最小命令与页面入口 |
| 账号删除 | `ArchivePageViewModel` → `DeleteGameAccount` → Repository `DeleteAsync` | 由范围 purge 替代父表 cascade |
| 档案删除 | `ArchivePageViewModel` → `DeletePlayerArchive` → Repository `DeleteAsync` | 由范围 purge 替代父表 cascade，保护幸存档案 |
| 通行证删除 | `UserPageViewModel` → Passport store | 只删除凭据，不删除 Gacha 档案；保持现状 |

低层 `IGachaRecordRepository.SaveBatchAsync` 当前仍是三条生产写入旁路；
`DeleteGameAccount` 和 `DeletePlayerArchive` 当前仍是两条父实体删除旁路。
8B.4 完成前必须由可维护的编译边界或架构测试将生产调用者清零。

## Q1～Q7 落地摘要

1. 刷新：抑制命中项、继续正常项、报告抑制数。
2. UIGF：允许补空并生成 Revision；不同非空事实冲突，不新增通用覆盖入口。
3. 新父实体／别名导入整体不承诺 Undo；已有账号纯事实新增按现有规则。
4. 人工纠正后的当前来源为 `UserEntered`；不伪造采集／导入时间。
5. 删除抑制按当前本地档案副本；同档案重建仍抑制已知事实，新档案独立。
6. Unresolved 使用本地副本最小 marker，不伪造自然身份。
7. Undo 显式重引入时恢复原活动 marker，并重验原 marker/version。

## 工作包状态

| 工作包 | 状态 | 说明 |
|---|---|---|
| P0 基线、矩阵、契约 | Completed | 本文和 ADR 0014；421／421 基线 |
| P1 原子前置条件 | Completed | expected-absent、版本 ABA、after-content、专用操作拒绝、Portable 容量确认；427／427 |
| P2 受控大文件暂存 | Partial | 已完成有限缓冲私有暂存、容器上限、可重开 lease 与兼容增量指纹；磁盘关联索引／分页 Plan 尚未完成 |
| P3 接管生产获取 | Completed | 三种刷新、UIGF／小助手共用核心和兼容 JSON 走统一政策；自动新档案／账号与事实同事务；精确档案名；相关全量回归通过 |
| P4 删除闭环 | Completed | 普通删除、单条／账号／档案 purge，Resolved 档案身份与 Unresolved 本地账号 marker，共享 Undo 失效 |
| P5 纠正／历史／Undo UI | Completed (code/automation) | `UserEntered` 纠正、修订查看、普通／永久删除、最新 eligible Undo、0～1000 设置；页面人工走查未完成 |
| P6 Portable 文件产品流 | Completed (code/automation) | picker、暂存、预览／映射、冲突／marker 确认、Apply、私有文件复验后保存；双端系统 provider 尚未实机验收 |
| P7 关闭旧旁路 | Completed | production `SaveBatchAsync`、父实体 repository Delete 调用者为 0；架构反例测试保护边界 |
| P8 双端真实验收 | Partial / Blocked | 版本校验、448 项测试、双平台 Release build、Windows 启动通过；Windows picker 操作待人工确认，ADB 当前无设备，跨端往返未运行 |

## 本地提交记录

| 提交 | 工作包 | 交付 |
|---|---|---|
| `f70fcf7` | P0 | 固化 Q1～Q7、入口矩阵与 ADR 0014 |
| `a39e057` | P1 | expected-absent、after-content、ABA、重引入 Undo 与容量前置条件 |
| `3c14584` | P2（部分） | 有限缓冲私有暂存、容器哈希与增量语义指纹 |
| `cc517bc` | P3 | UIGF／刷新／兼容 JSON 统一原子事实政策 |
| `ec88831`、`99683f1` | P4 | 范围 purge、最小 Tombstone 与管理页删除闭环 |
| `92740a8` | P5 | Gacha 纠正、修订历史、Undo 与限额 UI |
| `fe4f6c5` | P6 | Portable 文件导入／导出产品流程 |
| `ede3c0e` | P7 | 移除低层生产写入／父实体删除接口旁路 |
| `dcb82cc` | P3 收口 | 自动创建父实体与事实同事务；精确名称与默认唯一约束 |
| `a49ad1a` | P8 | 软件版本推进到 `0.8.5`，PhaseLabel `8B.4` |
| `e06b628` | P7 收口 | concrete SaveBatch 内部化；移除 concrete 父实体 Delete；架构反例扩展 |

## Q1～Q7 实际实现

1. 刷新先识别活动 Tombstone；命中项从本次 mutation 集合移除，其余记录仍在一个原子操作提交。结果和页面均显示抑制数。
2. UIGF `PreserveExisting` 只补空；有效补空生成 Revision。不同非空事实整次 Conflict，不提供通用覆盖按钮。
3. 新档案／账号操作设置为不可撤销；确认文案提前说明。父实体与事实现在同事务创建，不拆分伪造 Undo。
4. 人工纠正固定账号和 External ID，当前事实来源写为 `UserEntered`；before Revision 保留原来源，不补造 FetchedAt／ImportedAt。
5. Resolved 删除 marker 使用档案＋共享角色身份＋External ID；同档案重建账号仍命中，新档案不命中。
6. Unresolved 删除使用档案＋本地账号＋External ID，不伪造 UID／区服／共享身份；补全身份后仍可解释原本地范围。
7. 显式重引入关闭原 marker；Undo 绑定原 marker/version 并重新激活，防止后续刷新静默恢复。

## 生产写入与删除边界

- UIGF 文件、提瓦特小助手自动／手动下载均进入 `ImportUigfGachaRecords` → `ICommitGachaRecords` → `IGachaAtomicChangeStore`。
- SToken、手工 URL、Windows Web 缓存均进入 `RefreshGachaRecords` → 同一 Gacha 写入政策；URL 来源不改变事实的 `OfficialApi` 来源。
- 兼容 JSON 保留时也使用 `ICommitGachaRecords`；不再注入可写 Gacha repository。
- Portable v1 Apply 复用同一个 `SqliteGachaAtomicChangeStore`，保持严格 AddOnly。
- 账号／档案不可逆删除只通过 Gacha scope purge；通行证凭据删除仍不删除业务档案。
- 服务可见的 `IGachaRecordRepository` 没有 `SaveBatchAsync`；SQLite／内存 concrete 仓库的 legacy `SaveBatchAsync` 已改为仅测试友元程序集可见。`IGameAccountRepository`／`IPlayerArchiveRepository` 及其 SQLite concrete repository 均没有父实体 Delete。关系级 cascade 测试只在测试程序集以直接 SQL 验证 Schema，不构成生产入口。
- Schema migration、测试 fixture 和测试专用内存 committer 是隔离例外，不属于生产旁路。

## P2 未关闭原因

当前 `FileGachaPortableInputStager` 已用 64 KiB buffer 将非 seekable provider 内容复制到私有文件，并在复制时校验容器 byte 上限及 SHA-256；坏包、取消和异常会删除暂存文件。

但生产 `GachaPortablePackageReader` 仍把每账号 UIGF 和 supplement 合并为完整 `IReadOnlyList<GachaRecord>`；`PlanGachaPortableImport` 仍保存全部冲突；`SqliteGachaAtomicChangeStore.ApplyAsync` 仍为整包准备 mutation 集合。因此当前实现不满足“Reader／Plan／conflict／mutation 均不整体物化”，也没有 100k 双端峰值内存证据。`2,000,000` 仍只是格式解析安全上限，不是产品运行承诺。不得通过降低上限、只分批写入或只把源文件落盘宣称 P2 完成。

后续最小安全实现应是一次导入会话专用的私有 SQLite（或等价磁盘索引）：增量解析标准与 supplement、以账号＋External ID 关联、分页 Plan／冲突、事务内分批读取但单业务事务 Apply，并随会话取消／完成清理。它不是长期领域库或第二套写入体系。

## P8 当前证据（2026-10-08）

- `dotnet test ... --configuration Release --no-restore`：445／445 通过（移除 3 个不再属于仓库契约的 legacy Delete 测试后；父实体 purge/cascade 仍由专用测试覆盖）。
- `build/Verify-Versioning.ps1`：通过；人工版本源为 `0.8.5`／`8B.4`。
- Windows Release build：`net10.0-windows10.0.19041.0`，0 警告、0 错误；Windows 11 Pro `10.0.26200` 上进程启动并保持响应。系统 picker／saver 与真实文件往返：**Not run / awaiting manual interaction**。
- Android Release build：`net10.0-android`，SDK `E:\AndroidSDK`，0 警告、0 错误。构建产物实际包名 `com.companyname.furinachronicle.app`、`versionName=0.8.5`。ADB 检查无设备：真机安装、Documents provider、后台／返回／重建：**Blocked**。
- Windows → Android → Windows 和反向真实 Portable 文件逐字段往返、100k 文件、提交前后进程终止、空间不足：**Not run**。
- 未执行 `adb uninstall`、`pm clear` 或任何会清除现有手机数据的操作。

## Exit Criteria 核对

| 编号 | 状态 | 说明 |
|---|---|---|
| 1 | Pass (automation/code) | 生产入口清单完整；legacy Save/Delete 无生产调用者 |
| 2 | Pass (automation/code) | 所有已实现来源有 SQLite fixture；自动父实体与事实同事务 |
| 3～7 | Pass (automation/code) | 变化分类、版本、Undo、purge、Portable 产品流均有反例；实际 UI／provider 证据另列 |
| 8 | **Fail / Partial** | Plan、conflict、mutation 仍整体物化；无 100k 双端测量 |
| 9 | **Blocked** | ADB 无设备；Windows 文件服务尚未人工确认；双向传递未运行 |
| 10 | Pass (automation) | 既有导入导出、统计、真实抽数和独立查询回归纳入 445 项测试；人工回归未完整执行 |
| 11 | Partial | 迁移/fail-safe fixture、版本校验和双平台 build 通过；双平台 publish／真实旧库与设备验收未在本轮完整执行 |
| 12 | Pass | Q1～Q7 已进入 ADR 0014；本表区分代码、自动化和设备证据 |

结论：8B.4 **不能标记 Completed**。可以把 P0、P1、P3～P7 视为代码／自动化工作包完成；P2 与 P8 是剩余关闭门槛。

## 证据纪律

每个工作包完成后记录实际提交、自动化和平台证据。Build、CI、fixture、模拟器和
真机证据分别标注；未实际执行的项目写 `Not run` 或 `Blocked`。完整 P8 未满足前，
不得将整个 8B.4 标为 Completed。
