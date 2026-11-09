# Archive RestorePlan v1

状态：Accepted internal implementation contract；未公开发布

适用阶段：Phase 8C.2

## 目的

RestorePlan 是一个不写目标数据库的、可审查且可失效的恢复预览。它把一个已经通过 Archive v1 Reader 验证的输入，与一个目标 SQLite 一致快照进行比较，明确说明父对象映射、增量、冲突、Tombstone、设置、空间和撤销资格。

RestorePlan 不是导入结果、Receipt、Undo 材料或同步 ChangeSet。8C.2 只生成和重新验证 Plan；8C.3 才能在单一跨领域事务中 Apply。

## 状态机

```text
Picker Stream
  → app-private copy + package SHA-256
  → Archive Reader validation
  → private bounded index
  → target SQLite online-backup snapshot
  → mapping and semantic comparison
  → immutable RestorePlan
  → Preview / cancel / re-plan
```

失败、取消和释放 session 必须删除私有输入副本与 staging 数据库。任何步骤均不得创建目标档案／账号、写 Receipt、停用 Tombstone 或改变设置。

## Plan 绑定内容

每个 Plan 至少绑定：

- Archive 包 SHA-256 和内部格式版本；
- 目标 Schema 版本和目标一致状态令牌；
- Merge 模式；首版不包含 Replace；
- 每个源档案的目标映射或稳定的新建提议；
- 每个源账号的目标映射、共享身份处理或未解析状态；
- 来源当前事实、Revision 与目标事实的 Add／Skip／Conflict 分类；
- 活动 Tombstone key/version 及是否已提供显式重引入确认；
- 来源/目标可移植设置差异及设置选择；
- 未知可选 Dataset 的 partial restore 确认；
- staging、预计数据库增长和预计 Undo 材料空间；
- 整体 Undo 资格及原因；
- 由上述内容确定性计算的 Plan 指纹。

重新选择输入、映射、设置政策、确认或模式必须生成新 Plan。8C.3 Apply 前必须用新的目标一致快照重验目标令牌和全部 marker/version；记录数相同不能代替状态令牌。

## 映射规则

### 档案

1. 名称使用完整字符串 Ordinal 精确匹配，不 trim、不忽略大小写。
2. 一个精确候选可自动映射。
3. 多个精确候选必须显式映射或选择新建。
4. 没有精确候选但存在相似名称时，只报告候选并要求显式选择映射或新建。
5. 没有候选时可以新建；唯一名称约束下，来源或计划内重名使用稳定的 ` (2)`、` (3)` 编号并在 Preview 中报告。
6. 显式目标不存在、目标被重复使用而会合并两个来源档案，或新名称违反唯一约束时 Plan 冲突。

### 账号与身份

- 已解析账号只按 `GameRoleNaturalIdentity` 在已选目标档案中匹配；显示名、头像和来源本地账号 GUID不参与自动匹配。
- 同一自然身份可以在不同目标档案形成独立账号副本；共享角色身份可以复用。
- 来源稳定身份 GUID 在目标指向不同自然身份时是阻塞冲突。
- 一个来源档案内重复自然身份是阻塞冲突。
- Unresolved 不伪造自然身份，也不按占位 UID/名称猜测。新建档案时可以保持 Unresolved 创建；已有档案中的可能 Unresolved 候选要求显式决定。

## 事实、Revision 与 Tombstone

- 稳定事实 ID 不存在于目标时为 Add；内容完全相同为 Skip；相同 ID 不同可移植语义为 Conflict。
- Revision 以其稳定 Revision ID 比较；同 ID 不同 before/after、版本或时间为 Conflict。
- Gacha 标准事实与 `standard_extension` 已在 Reader 阶段合并为一个逻辑事实，不重复计数。
- 当前事实和 Revision before/after 都生成目标 Tombstone probe。任一 probe 命中活动 marker 时，默认不能 Apply；显式确认必须绑定该 marker key/version 和具体事实引用。
- RestorePlan 只报告确认需求，不停用 Tombstone，不把来源 Tombstone 传播到目标。

## 设置与 Undo

- 设置白名单仍只有全局 `RequireUniqueArchiveNames` 和每档案 `UndoLimit`。
- 已有目标默认保留目标设置；新建档案使用来源 `UndoLimit`。选择应用来源设置时，Plan 必须列出实际差异。
- 只有“全部目标父对象已存在、仅新增当前事实、没有来源 Revision、没有设置变化、没有身份别名且没有 Tombstone 重引入”的操作可标记为 Undo 候选；否则整个操作不承诺普通 Undo。

## 诊断和隐私

- 冲突和警告使用结构化代码、稳定引用和有限预览；不得在异常或日志中输出完整业务载荷、凭据、AuthKey 或 Cookie。
- 未知可选 Dataset 在容器级校验通过后产生 partial 诊断；未显式确认时 `CanApply = false`。不得把 partial 计划报告为完整恢复。
- 资源估算是防止明显空间不足的计划信息，不是精确磁盘承诺。Apply 必须重新检查实际空间和容量确认。

## 8C.2 验收

- 坏包、future major、路径攻击、哈希错误和超限在 staging 阶段拒绝且目标不变；
- 同名、近似名、多个同名、同 UID 跨档案、同 GUID 不同身份和 Unresolved 均按上述规则产生确定计划；
- 当前事实、Revision、空父对象、设置差异和 Tombstone 命中进入同一个 Plan；
- 目标内容变化但计数不变时，状态令牌变化并使旧 Plan 失效；
- 重复规划相同输入、目标和选择得到相同 Plan 指纹；
- Preview 取消或 session 释放不写目标并清理私有文件；
- Chronicle 和网络不可用不影响 staging 与 planning。
