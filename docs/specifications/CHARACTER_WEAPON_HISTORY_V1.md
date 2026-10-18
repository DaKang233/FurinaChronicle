# 角色／武器历史与 Portable v1

状态：Accepted implementation；内部格式未发布

本规范的持久化和 Portable 语义依赖 Accepted ADR 0016。v1 字段由 [JSON Schema](schemas/character-weapon-history-v1.schema.json)、示例和自动化共同冻结；完成 Windows↔Android 真实文件往返前仍不对外宣称公开发布。

## 数据集

- `character-weapon-observations`：不可变状态观察、范围和条目；
- `manual-acquisitions`：稳定手工事实及其来源修订链。

## 完整度

观察头声明项目种类和集合覆盖：`Complete` 表示该来源在该次观察声明的角色或武器集合已全部枚举；`Partial` 表示只覆盖已列项目；`Unknown` 表示无法证明集合边界。

每个条目另行声明字段集合。条目 `Complete` 只表示所声明的等级／命座或等级／精炼／数量字段均由来源提供。未知字段使用空值，不编码为零。任何低层完整度都不提升为账号仓库、账号历史或档案完整。

## 可读导出

首版提供 Markdown，列出档案、账号、观察引用、观察／抓取时间、来源、集合范围、条目字段、明确的“未提供”空值和 Derived 比较。比较只说明两个观察点之间的变化及确定性，报告不得把后一次观察时间显示成升级或获得时间。

## Portable v1

Portable 保存来源账号自然身份和共享身份引用、全部不可变观察、手工当前事实及修订链。目标数据库重新生成本地 `GameAccountId`，保留稳定观察／获取引用；来源 provenance 与目标接收 Receipt 分开。

不包含本机 Undo、UndoLimit、OperationId、ChangeSetId、LocalOnlyTombstone、图标缓存、显示名称或 SQLite Row。修订使用自身稳定 `RevisionId` 串联事实版本；导入时重新归属目标端本地 ChangeSet。future major、未知枚举、悬空引用、同 ID 不同内容和资源超限在写数据库前结构化拒绝。

- 格式名：`furina-domain-history`；`dataset_kind = 1`；版本 `1.0`。
- 单文件 JSON，最大 10 MiB、最多 100,000 个当前事实／修订；读取使用 64 KiB 分块并在反序列化前限制大小。
- `package_fingerprint` 是规范 JSON 内容的 SHA-256，用于损坏与预览后替换检测，不声明作者真实性。
- Plan 只读；Apply 重验指纹、账号映射、目标计数、Tombstone 和修订身份，父实体、事实、修订、Receipt、ChangeSet 同事务。
- 同包再次 Apply 为 NoOp；同稳定引用相同内容跳过，不同内容 Conflict。没有 v0 或旧版 Migrator；future major 拒绝。
- 额外未知属性为 minor 扩展保留；当前实现遇到未知枚举或无法构造的语义值时拒绝。

最小、完整、损坏和 future-major 文件位于 `examples/`。空账号列表的 minimum 示例仅证明 Envelope/Codec 最小结构，不是可 Apply 的业务数据集；Apply 至少需要一个受支持事实。
