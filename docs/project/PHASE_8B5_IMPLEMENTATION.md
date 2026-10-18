# Phase 8B.5 实施契约与验证记录

状态：Proposed implementation contract

实施状态：P0 contract review

基线：`feat/phase-8 @ 6b3a8fe0a7bec8592da31a00e1e31512d17dcb8d`

## 本轮边界

8B.5 建立角色／武器状态观察、手工获取事实及首批深境螺旋挑战观察的独立窄纵切。每个领域必须能够在不注册 Chronicle 的情况下完成保存、查询、历史查看、人类可读导出和 Portable 重导入。

本轮不实现米游社在线采集、完整领域 UI、全部挑战模式、Chronicle Contributor、完整 Archive、Sync／ChangeJournal 或服务端。Snap.Hutao.Remastered 仅用于理解可取得字段和产品信息粒度，外部 DTO、显示名称和 UI 结构不得进入 Core 身份。

## P0 基线

- Release 测试：462／462 通过（2026-10-18）。
- 主数据库：Schema 5；未知／未来版本和错误 application ID 已 fail-safe 拒绝。
- 软件版本：`0.8.5`；PhaseLabel `8B.4`。版本只在 8B.5 形成实际软件里程碑时推进。
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

## 待维护者确认的 Proposed 语义

[ADR 0016](../decisions/0016-character-weapon-observations-and-acquisitions.md) 和 [ADR 0017](../decisions/0017-challenge-observation-history.md) 当前为 Proposed。首次创建对应生产 Schema 或 Portable 格式前必须确认：

1. 手工 Week 使用周一 00:00 开始、固定来源偏移（默认 UTC+8）的 `[start,end)` 日历周；
2. 观察重试、A→B→A、迟到和同刻冲突政策；
3. 角色／武器集合与字段完整度，以及无实例 ID 武器采用类型汇总；
4. Portable 携带全部不可变观察历史和手工事实修订，不携带本机 Undo 槽、限额或 Tombstone。

在上述语义确认前，可以实现跨领域协调器、纯值对象和反例，但不得将依赖选择的字段写入生产数据库或宣称 Portable v1 已冻结。

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
| P0 契约与反例 | In progress | 基线通过；ADR 0016／0017 等待维护者确认 |
| P1 跨领域历史与 purge | Pending | 不依赖 Portable 公开格式，但必须先于新增领域生产写入 |
| P2～P4 角色／武器纵切 | Blocked on P0 semantics | 可先完成纯模型和协调反例，持久化／格式等待确认 |
| P5～P6 挑战纵切 | Blocked on P0 semantics | 持久化／格式等待确认 |
| P7 综合回归与交接 | Pending | 人工设备项单列，不用自动化冒充 |

