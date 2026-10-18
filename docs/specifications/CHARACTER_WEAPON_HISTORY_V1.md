# 角色／武器历史与 Portable v1 草案

状态：Draft；内部格式未发布

本规范的持久化和 Portable 语义依赖 Proposed ADR 0016。字段名称在 P0 评审后冻结。

## 数据集

- `character-weapon-observations`：不可变状态观察、范围和条目；
- `manual-acquisitions`：稳定手工事实及其来源修订链。

## 完整度

观察头声明项目种类和集合覆盖：`Complete` 表示该来源在该次观察声明的角色或武器集合已全部枚举；`Partial` 表示只覆盖已列项目；`Unknown` 表示无法证明集合边界。

每个条目另行声明字段集合。条目 `Complete` 只表示所声明的等级／命座或等级／精炼／数量字段均由来源提供。未知字段使用空值，不编码为零。任何低层完整度都不提升为账号仓库、账号历史或档案完整。

## 可读导出

首版提供 Markdown，列出档案、账号、观察引用、观察／抓取时间、来源、集合范围、字段空值和 Derived 比较。报告不得把后一次观察时间显示成升级或获得时间。

## Portable 范围草案

Portable 保存来源账号自然身份和共享身份引用、全部不可变观察、手工当前事实及修订链。目标数据库重新生成本地 `GameAccountId`，保留稳定观察／获取引用；来源 provenance 与目标接收 Receipt 分开。

不包含本机 Undo、UndoLimit、OperationId、LocalOnlyTombstone、图标缓存、显示名称或 SQLite Row。future major、未知枚举、悬空引用、同 ID 不同内容和资源超限在写数据库前结构化拒绝。

