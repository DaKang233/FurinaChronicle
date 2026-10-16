# Phase 8B.4 实施契约与验证记录

状态：Accepted implementation contract

实施状态：In progress（2026-10-17；代码与自动化完成，设备验收待关闭）

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
| P2 受控大文件暂存 | Completed (code/host automation) | 产品入口使用会话私有 SQLite：流式关联 UIGF/supplement、分页 Plan/冲突、两遍分页 Apply；第二遍仍在一个主数据库事务中提交 |
| P3 接管生产获取 | Completed | 三种刷新、UIGF／小助手共用核心和兼容 JSON 走统一政策；自动新档案／账号与事实同事务；精确档案名；相关全量回归通过 |
| P4 删除闭环 | Completed | 普通删除、单条／账号／档案 purge，Resolved 档案身份与 Unresolved 本地账号 marker，共享 Undo 失效 |
| P5 纠正／历史／Undo UI | Completed (code/automation) | `UserEntered` 纠正、修订查看、普通／永久删除、最新 eligible Undo、0～1000 设置；页面人工走查未完成 |
| P6 Portable 文件产品流 | Completed (code/automation) | picker、暂存、预览／映射、冲突／marker 确认、Apply、私有文件复验后保存；双端系统 provider 尚未实机验收 |
| P7 关闭旧旁路 | Completed | production `SaveBatchAsync`、父实体 repository Delete 调用者为 0；架构反例测试保护边界 |
| P8 双端真实验收 | Partial | 版本校验、460 项测试及宿主 100k 测量通过；双平台 Release build 已重新验证，Windows/Android 真实 Portable 文件、跨端往返、进程终止和空间不足仍待人工确认 |

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
| `52e6a3b` | P2 收口 | 会话私有 SQLite、流式关联、分页 Plan/冲突及单事务分页 Apply |

## Q1～Q7 实际实现

1. 刷新先识别活动 Tombstone；命中项从本次 mutation 集合移除，其余记录仍在一个原子操作提交。结果和页面均显示抑制数。
2. UIGF 默认 `PreserveExisting` 只补空；有效补空生成 Revision。不同非空事实整次
   Conflict。ADR 0015 后，用户显式开启统一全量导入模式时可改用覆盖策略；默认行为
   不变。
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

## P2 实际关闭方式

`FileGachaPortableInputStager` 继续用 64 KiB buffer 将 provider 内容复制到私有文件并计算容器 SHA-256。产品 UI 不再从 DI 取得完整模型 `IGachaPortablePackageReader`；该 Reader 只保留为兼容 Codec／小型 fixture。生产流程改为：

1. `GachaPortableIndexedPackageFactory` 增量读取 supplement NDJSON 和 UIGF token，将关联后的规范记录写入一次会话专用的私有 SQLite；
2. `PlanGachaPortableImport` 每 500 条查询目标并把 disposition、差异和覆盖前快照写回索引；公开 Plan 只保留计数和最多 12 条冲突预览，完整冲突通过分页接口读取；
3. `SqliteGachaAtomicChangeStore` 第一遍分页重验并汇总容量，第二遍分页重建 mutation、写事实／Revision／Undo／Receipt；两遍均位于同一个主数据库事务，任一后段故障回滚父实体和已经写入的事实；
4. 会话完成、取消或解析失败时关闭并删除索引与源暂存，不形成第二套长期数据源。

自动化覆盖 1,250 条完整 Apply、20 条分页冲突／全量覆盖、第二遍写入 500 条后故障回滚、取消清理，以及 100,000 条 Reader＋Plan。2026-10-17 Windows x64 测得 100k 容器为 1,482,268 bytes，相对测试基线的托管内存采样峰值增量为 18,236,048 bytes。这个数值是当前宿主证据，不是跨设备性能承诺；Android 与 Windows UI 进程的实际峰值仍属于 P8 人工验收。`2,000,000` 继续只是安全上限。

## P8 当前证据（更新至 2026-10-17）

- `dotnet test ... --configuration Release --no-restore`：460／460 通过；新增生产索引、分页冲突／覆盖、后段事务回滚、取消清理和 100k Reader＋Plan 覆盖。
- `build/Verify-Versioning.ps1`：通过；人工版本源为 `0.8.5`／`8B.4`。
- Windows Release build：`net10.0-windows10.0.19041.0`，0 警告、0 错误；Windows 11 Pro `10.0.26200` 上进程启动并保持响应。系统 picker／saver 与真实文件往返：**Not run / awaiting manual interaction**。
- Android Release build：`net10.0-android`，SDK `E:\AndroidSDK`，0 警告、0 错误；最终产品代码 APK SHA-256 为 `34A21E39DEBEA3EC5AB9A1C8CF590F365BF022221D478E2A2DFD206CBB4D952B`。已通过 ADB 在 vivo V2353A（Android 14／SDK 34，约 7.24 GiB RAM）保留数据覆盖安装，设备报告 `versionCode=113`、`versionName=0.8.5`；主 Activity 启动并保持前台。Android Documents picker 已实际打开，并通过系统返回取消后安全恢复到 FurinaChronicle 主界面；进程错误通道未见 AndroidRuntime／Mono／.NET 未处理异常。后台杀进程后的会话重建和真实文件 Apply：**Not run**。
- Windows → Android → Windows 和反向真实 Portable 文件逐字段往返、100k 文件、提交前后进程终止、空间不足：**Not run**。
- 未执行 `adb uninstall`、`pm clear` 或任何会清除现有手机数据的操作。

## Exit Criteria 核对

| 编号 | 状态 | 说明 |
|---|---|---|
| 1 | Pass (automation/code) | 生产入口清单完整；legacy Save/Delete 无生产调用者 |
| 2 | Pass (automation/code) | 所有已实现来源有 SQLite fixture；自动父实体与事实同事务 |
| 3～7 | Pass (automation/code) | 变化分类、版本、Undo、purge、Portable 产品流均有反例；实际 UI／provider 证据另列 |
| 8 | **Pass (code/host automation) / device Partial** | 产品 Reader、Plan/conflict、mutation 不再整体物化；宿主 100k 测量通过，双端 UI 进程实测待 P8 |
| 9 | **Partial** | Windows 与 Android 真机均已启动；Android Documents picker 打开／取消恢复通过，Windows picker 和双向传递未运行 |
| 10 | Pass (automation) | 既有导入导出、统计、真实抽数和独立查询回归纳入 445 项测试；人工回归未完整执行 |
| 11 | Partial | 迁移/fail-safe fixture、版本校验和双平台 build 通过；双平台 publish／真实旧库与设备验收未在本轮完整执行 |
| 12 | Pass | Q1～Q7 已进入 ADR 0014；本表区分代码、自动化和设备证据 |

结论：8B.4 的非人工代码与自动化工作包 P0～P7 已关闭；整个 8B.4 **仍不能标记 Completed**，唯一剩余关闭门槛为 P8 真实 Windows／Android 文件与设备验收。

## 证据纪律

每个工作包完成后记录实际提交、自动化和平台证据。Build、CI、fixture、模拟器和
真机证据分别标注；未实际执行的项目写 `Not run` 或 `Blocked`。完整 P8 未满足前，
不得将整个 8B.4 标为 Completed。
