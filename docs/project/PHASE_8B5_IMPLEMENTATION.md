# Phase 8B.5 实施契约与验证记录

状态：Accepted implementation contract

实施状态：Implementation completed；device file evidence deferred to 8B.7

基线：`feat/phase-8 @ 6b3a8fe0a7bec8592da31a00e1e31512d17dcb8d`

## 本轮边界

8B.5 建立角色／武器状态观察、手工获取事实及首批深境螺旋挑战观察的独立窄纵切。每个领域必须能够在不注册 Chronicle 的情况下完成保存、查询、历史查看、人类可读导出和 Portable 重导入。

本轮不实现米游社在线采集、完整领域 UI、全部挑战模式、Chronicle Contributor、完整 Archive、Sync／ChangeJournal 或服务端。Snap.Hutao.Remastered 仅用于理解可取得字段和产品信息粒度，外部 DTO、显示名称和 UI 结构不得进入 Core 身份。

## P0 基线

- Release 测试：462／462 通过（2026-10-18）。
- 主数据库基线为 Schema 5；本轮以显式 5→6 迁移增加新领域私有事实、修订和 Receipt，v1～v5 均有到 6 的测试路径。未知／未来版本和错误 application ID 继续 fail-safe 拒绝。
- 软件版本已推进为 `0.8.6`；PhaseLabel `8B.5`。Build 继续只由 Git commit count 派生。
- 8B.4 已完成；100k 设备压力测试是非阻塞延期项。
- 当前操作历史、UndoLimit、Undo 分派和父范围 purge 仍由 Gacha 原子 Store 承担，必须在新增领域生产写入前抽取有限的跨领域协调边界。

## 最小模型与范围

### 角色／武器状态观察

- 所有事实归属于档案内 `GameAccountId`，不因共享 `GameRoleIdentityId` 跨档案共享。
- 可玩角色／武器使用稳定类型 ID，不使用名称、图标或本地化文本作为身份。
- 角色条目最小字段：类型 ID、可空等级、可空命之座等级、字段覆盖声明。
- 武器条目最小字段：类型 ID、可空实例 ID、可空数量、可空等级、可空精炼等级、字段覆盖声明。
- 无可靠武器实例 ID 时只保存类型汇总；不得用数组位置配对同种武器实例。
- 不可变观察使用稳定 `ObservationId`；规范内容指纹只用于幂等和冲突判断，不兼任身份。
- 观察范围分别声明集合覆盖与条目字段覆盖。`Complete` 只对声明的项目种类和字段集合成立；`null` 不等于 `0`，Partial 中未出现项目不表示消失。
- 比较结果是 `Derived`，引用两份观察及规则版本。真实变化时间通常表示为两个观察之间的不确定区间；首次观察不推导获得时间。

### 手工获取事实

- `AcquisitionId` 是内容变化时保持不变的稳定身份；相似内容只提示，不静默合并。
- 角色原因代码：砺行修远、周年庆自选常驻五星、周年庆自选限定五星、其他。周年庆活动名可空；其他原因说明必填。
- 武器使用非空自由文本原因，不复用角色原因枚举。
- 来源固定为 `UserEntered`；时间精度支持 Second／Minute／Hour／Day／Week。
- 纠正、普通删除和 Undo 与 ChangeSet 同事务；重复同内容为 NoOp，同 ID 不同内容的 AddOnly 为 Conflict。
- 手工事实不与近似时间的 Gacha 或状态观察自动合并。

### 挑战观察

- 首批实际支持模式仅深境螺旋。期次自然键为目标 `GameAccountId + mode code + official season id`。
- 周期是可空的来源事实，不由当前游戏规则重建过去周期。
- 不可变观察保存稳定引用、规范业务指纹、覆盖范围、来源、ObservedAt／FetchedAt 及可取得的子关卡事实时间。
- 业务指纹排除 HTTP 时间、导入时间、采集批次、对象属性顺序以及业务无序数组的输入顺序。
- Partial 不覆盖或降级 Complete；非包含关系的 Partial 不合成为未经证明的 Complete。
- 缺少可靠完成时间时 `OccurredAt` 保持 Unknown；周期末、观察时间和抓取时间不得冒充完成时间。
- 8B.5 不冻结完成节点条件，只保存 8B.6 判断所需证据。

## 已确认语义

[ADR 0016](../decisions/0016-character-weapon-observations-and-acquisitions.md) 和 [ADR 0017](../decisions/0017-challenge-observation-history.md) 已于 2026-10-18 接受：

1. 手工 Week 使用周一 00:00 开始、固定来源偏移（默认 UTC+8）的 `[start,end)` 日历周；
2. 观察重试、A→B→A、迟到和同刻冲突政策；
3. 角色／武器集合与字段完整度，以及无实例 ID 武器采用类型汇总；
4. Portable 携带全部不可变观察历史和手工事实修订，不携带本机 Undo 槽、限额或 Tombstone。

具体私有表结构和内部格式字段仍须由实现、Schema 和反例共同固定；未完成双平台往返前不得宣称 Portable 已公开发布。

## 生产入口与协调矩阵

| 能力 | 当前入口 | 8B.5 目标 |
|---|---|---|
| 档案操作历史查询 | `IGachaAtomicChangeStore.GetHistoryAsync` | 中立档案历史端口；Gacha facade 保持兼容 |
| 最新撤销 | `IGachaAtomicChangeStore.UndoLatestAsync` | 选择档案最新 eligible 操作并按有限实体类型分派；未知类型整项拒绝 |
| UndoLimit／容量清理 | Gacha Store 内部 | 仍按档案共享 0～1000 限额和最旧优先材料清理，不建立领域独立计数 |
| 账号／档案 purge | `PurgeAccountAsync`／`PurgeArchiveAsync` | 单一 SQLite 事务清除所有已实施领域的 current、可恢复快照和专用 Undo，再保留最小本地 marker |
| 角色／武器观察写入 | 尚无 | 原子领域用例；只读 repository 不向生产调用者暴露写旁路 |
| 手工获取生命周期 | 尚无 | Add／Correct／Delete／Undo 专用用例，共享 OperationHistory |
| 挑战观察写入 | 尚无 | 不可变观察提交；重复 NoOp、冲突报告、回返保留 |
| Portable Apply | 尚无 | Inspect→Validate→Plan→Preview→Apply→Verify；包指纹和目标状态重验，单事务 |

## 失败与数据安全

- 新 Schema 使用显式已知版本迁移；旧库不虚构观察、来源、时间或历史。
- 迁移、Apply、Undo 或 purge 任一步失败均回滚事实、父实体、历史和 marker。
- 未知实体类型、future major、未知枚举、悬空引用、超限、过期计划及同 ID 不同内容均结构化拒绝。
- 普通导入不因来源缺失删除本地事实；源 `GameAccountId` 必须重映射。
- 新领域 Portable 在双平台真实语义往返前保持内部支持／未发布。

## 必需反例

P0 fixture 编号沿用实施计划的 S01～S04、M01～M05、C01～C06、I01～I02、P01～P04、H01～H03、D01～D03、DB01～DB02 与 E01。测试必须断言业务内容、来源、范围、时间、版本、外键和失败后数据库状态，而不只断言未抛异常或行数。

## 工作包状态

| 工作包 | 状态 | 说明 |
|---|---|---|
| P0 契约与反例 | Completed | 基线通过；ADR 0016／0017 已由维护者接受 |
| P1 跨领域历史与 purge | Completed | `IArchiveOperationHistoryStore`、有限实体分派、跨域最新 Undo、共享限额、父范围 purge／Tombstone 已接管 |
| P2～P4 角色／武器纵切 | Completed | Domain、比较、Schema 6、分页查询、手工修订、Markdown、Portable v1、Schema／示例和重导入已实现 |
| P5～P6 挑战纵切 | Completed | 首批深境螺旋观察、相邻判重、A→B→A、迟到最佳证据、Markdown、独立 Portable v1 已实现 |
| P7 综合回归与交接 | Automated complete / device evidence deferred | 完整测试、版本检查和双平台 Release 构建已完成；新领域 Windows↔Android 文件往返等待 8B.7 提供可达入口或 harness |

## 实际实现

- Core：`ItemStateObservation`、角色／武器条目范围、无实例 ID 武器类型汇总、稳定手工获取事实、周边界、纯比较规则；`ChallengeObservation`、深境螺旋楼层／间和未知完成时间。
- Services：中立档案操作历史端口；三个原子提交请求；按账号／种类／期次／UTC 半开时间范围的 1～500 条分页查询；当前挑战最佳证据；有界 source adapter 和两个 Portable 端口。
- SQLite Schema 6：`ArchiveDomainFacts`、`ArchiveDomainRevisions`、`DomainPortableReceipts`；事实、修订、ChangeSet、Undo 材料、Receipt、父实体创建、purge 和 marker 使用同一事务。
- 输入：`furina-collector-fixture` 是 2 MiB／10,000 观察上限的内部适配器测试边界，不是用户交换格式，也不是已实现的在线 Provider。
- 可读出口：两个领域均可离线导出 Markdown；角色／武器包含字段覆盖、缺值和观察间派生变化，挑战包含周期、楼层／间、星数和实际／未知完成时间。
- Portable：`furina-domain-history` 中 `dataset_kind = 1/2` 两个独立 v1 数据集；单文件 10 MiB、100,000 事实／修订上限，64 KiB 分块读取、规范 JSON SHA-256、只读 Plan、目标重验和原子 Apply。

## 关键行为证据

- 相同不可变引用同内容 NoOp、不同内容 Conflict；手工事实纠正／删除保持 AcquisitionId 和单调版本，并可按档案最新操作 Undo。
- 角色／武器 Partial 不把缺失解释为删除；未知值保持 `null`；同种武器无实例关联时仅保存类型数量。
- 挑战同稳定来源记录、同可信时刻同内容及相邻可信观察同内容不增殖；同来源不同内容或同刻矛盾受控冲突；可信 A→B→A 三份均保留。
- 当前挑战证据优先已声明完整度，再按来源 ObservedAt，而不按 ImportedAt；迟到导入不倒退当前视图。
- Portable 源 `GameAccountId` 重映射；同自然身份已有账号正常合并；源修订 ID／时间保留，导入包装修订不重复进入再次导出。
- Plan 后目标计数／内容、身份、Tombstone 或包指纹变化时 Apply 拒绝；新档案／账号尚未提交即发生冲突时不留父实体。
- Gacha、手工事实和挑战共享同一档案 UndoLimit；未知实体类型拒绝整次 Undo；父范围不可逆删除清除可恢复内容并保持档案副本范围抑制。

## 明确未实施

米游社／HoYoLAB 在线 Provider、完整角色／武器与挑战 UI、幻想真境剧诗、幽境危战、Chronicle Contributor、完整 Archive、Sync／ChangeJournal 均不属于 8B.5。Snap.Hutao.Remastered 仅作为可取得数据粒度参考，没有复制其 DTO、UI 或元数据身份。

## 人工验收

代码和自动化完成不等于设备证据。当前 App 尚未提供这两个新领域的产品导入／导出入口，因此维护者现在无法从正式 UI 执行下列步骤；应在 8B.7 提供受控临时 harness 或正式入口后再验收：

1. 在 Windows 导出角色／武器和挑战各一个 Portable 文件，在 Android 导入并核对账号映射、观察数、手工修订数、时间偏移和 Markdown；再反向导出回 Windows 重复一次。
2. 对同一文件第二次导入，确认 Preview 为全 Skip、Apply 为 NoOp，且历史页没有重复修订。
3. 确认两端均不会把新格式显示成“已公开稳定”，并记录文件大小、耗时、峰值内存和任何系统文件选择器差异。

当前状态为 `Not run — deferred to 8B.7 entry/harness`，不是人工失败或被设备环境阻塞。这些文件往返证据在 8B.7 汇总；完成前两个新 Portable 数据集保持 internal / unpublished。由于本轮没有新增完整产品页面，在线采集和领域 UI 不作为 8B.5 人工退出条件。

## 自动化验证（2026-10-18）

- `dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj --configuration Release --no-restore`：493／493 通过。
- `build/Verify-Versioning.ps1`：通过；人工版本源为 `0.8.6`／`8B.5`，构建过程未回写版本文件。
- Windows `net10.0-windows10.0.19041.0` Release：通过，0 warning／0 error。
- Android `net10.0-android` Release（`AndroidSdkDirectory=E:\AndroidSDK`）：通过，0 warning／0 error；包含 Release 裁剪和程序集预编译阶段。
- `git diff --check`：作为最终提交前门槛执行。

以上仅证明自动化与平台编译。未运行 Windows／Android 产品 UI、真机文件选择器或跨端文件语义往返；不得将其改写为设备验收通过。
