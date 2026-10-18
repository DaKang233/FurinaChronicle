# 挑战观察历史与 Portable v1

状态：Accepted implementation；内部格式未发布

本规范依赖 Accepted ADR 0017。首批生产支持只包括深境螺旋。

## 观察范围

期次身份由目标账号副本、稳定模式代码和官方期次 ID 构成。观察保存独立引用、规范内容指纹、周期（可空）、覆盖范围、来源、ObservedAt／FetchedAt 以及来源明确提供的子关卡事实时间。

`Complete` 只能相对于声明的期次、楼层／间和字段集成立。官方来源不自动等于 Complete，单期 Complete 不表示账号挑战历史完整。

## 历史和当前视图

Portable 保存全部不可变观察。重复引用同内容为 NoOp，同引用不同内容为 Conflict；可信顺序证明的 A→B→A 保留第二个 A。迟到观察补充历史但不因导入时间成为当前最佳证据；同刻矛盾或缺少顺序证据使用结构化 Conflict／Ambiguous。

当前最佳证据视图是可重建派生结果，不替代历史。Partial 不覆盖 Complete，非包含关系 Partial 不自动合并为 Complete。

## 可读导出

首版提供 Markdown，按账号、模式和期次列出全部观察、周期、来源、楼层／间、星数、实际完成时间和未知项。不输出未经领域规则确认的“完成”节点，不用周期末或抓取时间填充 `OccurredAt`。

剧诗形状和未知完成时间只作为合成 fixture；未支持模式必须受控拒绝。

## Portable v1

- 机器契约见 [JSON Schema](schemas/challenge-history-v1.schema.json)；格式名 `furina-domain-history`，`dataset_kind = 2`，版本 `1.0`。
- 首版只接受 `SpiralAbyss`。剧诗和幽境危战枚举是未来领域入口，不代表生产支持。
- Portable 保存全部不可变观察和 provenance，不保存本机 Undo、Tombstone、操作重试结果或派生“当前”视图。
- 同 ObservationId 同业务内容、同稳定来源记录重试及相邻可信时点同内容均为 NoOp；同来源不同内容或同可信时刻矛盾为 Conflict；可信 A→B→A 保留。
- Plan 与 Apply 使用同一判重语义；目标改变、指纹改变、未知顺序或 future major 均在提交前拒绝。父实体、观察、Receipt 和 ChangeSet 同事务。
- 资源上限与角色／武器数据集相同：单文件 10 MiB、最多 100,000 个观察；当前没有旧版 Migrator。

公开发布仍需 Windows↔Android 文件语义往返；在此之前属于内部支持格式。
