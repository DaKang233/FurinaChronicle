# Archive Atomic Merge v1

状态：Accepted internal implementation contract；未公开发布

适用阶段：Phase 8C.3

## 输入和前置条件

Apply 只接受仍存活的 `IArchiveRestoreStagedPackage` 与 `CanApply=true` 的 Merge Plan。提交前
必须重算 Plan 指纹，并在同一个写事务中重验目标 Schema／状态令牌、父对象映射、活动
Tombstone marker/version、容量确认和唯一名称约束。staging 的包指纹与 Plan 不一致时拒绝。

## 单一事务

一个 Applied Merge 只产生一个目标 `DataChangeSet`，并关联所有受影响档案。事务按以下
逻辑提交，但任一步失败都整体回滚：

1. 新父实体和必要的目标本地身份别名；
2. Gacha、物品观察、手工获取和挑战当前事实；
3. Archive 中保留的 Gacha／手工获取来源 Revision；
4. 经确认的 Tombstone 重引入、白名单设置和适用的 Undo 材料；
5. OperationHistory、OperationCommitResult 和 Archive Restore Receipt／映射。

来源 ChangeSet、Undo 栈、Receipt、本地 Tombstone 和 Chronicle 数据不复制。来源 Revision
保留其可移植历史内容和稳定 RevisionId，但绑定新目标 ChangeSet；当前事实获得新的目标
本地版本令牌。

## Undo 和 NoOp

只有 Plan 已判定为纯既有父实体上的当前事实新增，且实时容量允许完整捕获材料时，整个
跨档案操作才可撤销。任何不可撤销子项使整个操作不可撤销，不能拆成多个伪操作。
NoOp 写 Receipt 但不写空 ChangeSet；重复 Merge 不新增事实、Revision 或历史。

## 取消、提交和显示

读取 staging、准备与事务首次写入前可以取消。开始写入后忽略普通取消直至事务提交或
回滚，避免把“取消已请求”误报成“没有提交”。提交成功以持久化 Receipt 为准；随后 UI
刷新、Chronicle 查询或重建失败是独立错误，不得回滚或删除已保存事实。

## Chronicle

恢复不导入来源 generation、epoch 或游标。事实写入使用现有触发器推进目标 SourceEpoch；
因此提交前取得的严格游标失效。Chronicle 仍可从事实离线重建，且不参与 Restore 正确性。

## 首版边界

本规范只实现 Merge，不实现 Replace、生产恢复 UI、云同步、在线 Provider 或 Archive
公开发布。所有测试使用合成数据或受控临时数据库。
