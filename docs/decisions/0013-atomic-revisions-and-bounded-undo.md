# ADR 0013：原子变更集、修订与有限撤销

状态：Accepted

日期：2026-10-06

## 背景

Phase 8B.2 已提供 Gacha Portable v1 的只读计划，但生产 Apply 不能通过依次调用各自提交的 Repository 实现。覆盖、普通删除、撤销、身份别名和接收上下文必须与业务事实位于同一 SQLite 事务；重复请求、旧写入口和不可逆删除也不能绕过撤销前置条件。

## 决定

- 一次用户可理解操作使用稳定 `OperationId`，提交一个 `DataChangeSet`。同一 `OperationId` 重试返回原提交结果，不重复修改。
- Gacha 当前事实的稳定引用是档案内 `GameAccount.Id + ExternalRecordId`；共享角色身份不替代副本范围。
- 新路径为每个当前事实维护单调递增的本地版本令牌。任何实际持久化变化都推进令牌；只有事实或有效来源的实质变化形成业务 Revision。
- 仅 `FetchedAt` 或采集批次变化不形成 Revision、不占撤销槽；完全相同输入是 NoOp。来源、导入时间或规范化事实变化形成 Revision。
- Revision 使用显式版本的 Gacha 私有快照格式，保存恢复当前事实和最低来源所需字段，不保存凭据，也不是 Portable Dataset。
- 撤销只允许选择档案最新且仍符合前置条件的可撤销 ChangeSet。撤销生成新的反向 ChangeSet，原项标记已撤销；首版不提供 Redo 或“撤销撤销”。
- 跨档案操作只有一个 ChangeSet。在任一涉及档案关闭新撤销、容量淘汰或发生冲突时，整项不可撤销，不允许部分执行。
- 每档案默认限额 3，合法值为 0～1000。设置 0 保留已有材料并停止为后续操作保存新材料；1～1000 调低时立即按最旧优先淘汰超额的专用撤销材料，不删除 Revision。
- 正常限额淘汰不要求确认；因空间不足提前清理仍有效的撤销材料必须先返回确认计划。拒绝或过期确认均不修改数据。
- 新增档案、账号、别名或事实属于真实变化；只保存重复接收尝试不创建 ChangeSet、Receipt 或撤销项。
- 不可逆删除清除当前事实、对应可恢复快照和专用材料，使关联操作整体失去撤销资格；只保留非内容摘要和按稳定本地事实范围派生的 Tombstone 哈希。
- 自动写入命中活动 Tombstone 时拒绝恢复。显式重引入必须使用绑定目标和 Tombstone 版本的确认；成功后 Tombstone 保留为非活动状态并关联重引入 ChangeSet。
- Portable AddOnly Apply 在事务内重验输入指纹、档案/账号状态和实际记录内容；来源 provenance 与本机接收 Receipt 分开保存。Portable v1 文件格式不增加 Revision 或 Undo 字段。

## 后果

- 业务写入、Revision、ChangeSet、撤销材料、别名和 Receipt 可以由一个 Infrastructure 写端口原子提交。
- 旧 Schema 行不获得虚构操作历史；首次新路径修改时以当时真实 current row 作为 before-image。
- 尚未全面接管的旧入口只维护最小版本令牌；这会保守地使旧撤销发生冲突，而不会伪装为安全。
- OperationHistory、Revision 与未来 ChangeJournal 保持分离；本 ADR 不实现同步协议或任意事件溯源框架。

## 测试要求

- 同数量内容变化、同内容新批次、A→B→A、跨档案一侧失效、UndoLimit=0、旧 Undo 遇不可逆删除；
- 同 OperationId 重试、事务各步骤故障、迁移失败、SQLite 空间错误均无半提交；
- Portable 包内容或目标状态在计划后变化时拒绝 Apply；
- 反向 ChangeSet 不进入可再次撤销栈，跨档案撤销始终整体成功或整体失败。
