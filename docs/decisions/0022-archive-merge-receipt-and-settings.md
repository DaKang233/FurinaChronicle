# ADR 0022：Archive Merge 的本地回执与设置状态

状态：Accepted

日期：2026-10-23

## 背景

ADR 0021 已要求 Archive 恢复产生目标本地 Receipt，并允许在 Preview 后应用白名单设置。
Schema 7 的 `OperationCommitResults` 只能回答一个操作是否提交，不能证明它对应同一个
Archive 包、同一个 RestorePlan 和同一组目标映射；`RequireUniqueArchiveNames` 也尚无
权威持久化位置，导出只能固定写出 `true`。

## 决定

1. Schema 8 新增目标本地 Archive Restore Receipt。Receipt 绑定 `OperationId`、包指纹、
   Plan 指纹、目标 ChangeSet、提交时间、结果计数以及源到目标的档案／账号映射。
2. 相同 `OperationId` 只有在包与 Plan 指纹一致时返回已提交结果；不一致时报告冲突。
   相同包可以按不同映射恢复到其他目标，不能用文件哈希全局封禁。
3. NoOp 也写持久化 Receipt，但不伪造 ChangeSet 或操作历史。Applied 只创建一个跨档案
   ChangeSet，并由一次 SQLite 事务提交全部父实体、事实、来源 Revision、设置、Tombstone
   变化、Undo 材料和 Receipt。
4. Schema 8 新增单例可移植应用设置，首版只保存 `RequireUniqueArchiveNames`，默认 `true`。
   新建、重命名、导出、规划和恢复共用该权威值。每档案 `UndoLimit` 继续使用现有表。
5. Receipt、目标 ChangeSet、本地事实版本、Chronicle epoch/generation 和本机并发令牌均为
   目标本地状态，不从来源 Archive 复制。

## 原因

独立 Receipt 能在提交后 UI 刷新失败或进程重启时可靠回答结果，并阻止模糊重试造成双写。
把设置落库则避免“文件表示允许重名，但业务入口仍永久拒绝重名”的分裂语义。两者都是
ADR 0006 和 0021 已接受行为的持久化实现，不扩大 Archive v1 的公开载荷范围。

## 迁移影响

- v7→v8 在单事务中创建设置与 Receipt 表，并插入默认 `RequireUniqueArchiveNames=true`。
- 旧库行为保持不变；没有历史 Receipt 被推断或补造。
- future Schema、错误 application id、损坏库和迁移失败仍拒绝继续写入并保留原库。

## 测试要求

- 新库和 v1～v7 升级都得到 Schema 8、默认设置和有效外键。
- 同操作同包同 Plan 可在重启后读取 Receipt；同操作不同 Plan 冲突。
- Receipt 写入失败、设置写入失败或任一领域失败时不留下父实体、事实、历史或设置变化。
- `RequireUniqueArchiveNames=false` 时业务入口与 Archive 导出读取同一值；重新启用前必须没有
  精确同名档案。
