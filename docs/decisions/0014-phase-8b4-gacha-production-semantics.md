# ADR 0014：Phase 8B.4 Gacha 生产写入、删除与重引入语义

状态：Accepted

日期：2026-10-08

## 背景

ADR 0013 已建立 Gacha 的原子 ChangeSet、Revision、有限撤销、
LocalOnlyTombstone 与 Portable AddOnly Apply，但 8B.3 明确保留了旧生产入口：
UIGF、SToken／URL／Windows 缓存刷新和兼容 JSON 仍可调用低层
`SaveBatchAsync`，账号和档案删除仍可直接依赖父实体 cascade。

Phase 8B.4 必须先固定这些入口的用户可见语义，再把它们迁移到同一套
Gacha 原子写入基础。本文只决定当前 Gacha 产品行为，不建立通用 EventStore、
同步 Journal、完整 Archive 或 Chronicle 基础。

## 决定

### 获取与导入

- 用户主动增量或全量刷新命中少量活动 Tombstone 时，抑制命中项，继续以一次
  原子操作提交其余正常记录，并明确报告抑制数量。刷新、手工 URL、Windows 缓存
  或小助手自动模式均不构成重引入授权。
- 全量刷新只纠正本次官方响应明确返回的事实；来源未返回的旧事实不删除，也不据此
  宣称终身历史完整。
- UIGF 保留 `PreserveExisting` 的补空兼容。补空导致实际保存状态变化时必须生成
  Revision；相同稳定引用上的不同非空事实默认报告冲突且不覆盖。本阶段不新增通用
  “确认后覆盖导入”入口。
- Portable v1 继续严格 AddOnly；不同事实阻止整包，不能暗转 Upsert。
- 导入若创建新档案、账号或身份别名，整个操作不承诺可撤销；已有账号上的纯事实
  新增可以按 ADR 0013 捕获 Undo。确认前必须如实提示，不拆成伪操作制造可撤销假象。

### 纠正与删除

- 人工纠正已有事实时，当前有效事实的来源为 `UserEntered`；原来源保留在 before
  Revision。不得虚构 `FetchedAt` 或 `ImportedAt`，也不提供缺少可靠 External ID 的
  手工抽卡生成器。
- 不可逆账号或档案删除仅在当前本地档案副本范围建立抑制。同一档案中重新建立同一
  共享角色副本时，已永久删除的已知事实仍受抑制；新档案是独立副本，不作全局封禁。
  本阶段不创建“以后所有未知记录永久禁止导入”的范围标记。
- `Unresolved` 账号使用本地副本范围的最小 Tombstone／purge 标记；不得伪造 UID、
  区服或共享角色身份。清除后的具体内容不可恢复，未来身份补全仍应能解释该本地
  删除范围。
- 显式重引入使 Tombstone 转为非活动。若随后撤销这次重引入，必须在原 marker 和
  marker version 前置条件仍成立时恢复删除前的活动 Tombstone，避免后续自动刷新
  静默恢复事实。

### 安全顺序与边界

- 所有实际采集入口先遵守 Tombstone 抑制，之后才可开放父实体不可逆删除产品入口。
- 一个用户操作使用一个稳定 `OperationId` 和一个业务事务；提交成功后的展示失败不
  触发补偿删除，重试返回原提交结果。
- 普通原子 Commit 不接受伪造的 `Undo` 或 `IrreversibleDelete` 操作种类；这些行为只
  通过专用命令执行。
- 低层 `SaveBatchAsync` 和父仓储 `DeleteAsync` 只能保留在迁移、fixture 或明确隔离的
  测试边界；生产调用面在 8B.4 结束前关闭。

## 后果

- 刷新可以在不恢复永久删除事实的前提下保存其余正常记录，并向用户报告抑制。
- UIGF 补空会成为可审计的事实变化；不同非空事实不会被旧 PreserveExisting 静默吞掉。
- 父实体删除需要按历史范围 purge，而不能只依赖当前表外键级联。
- Tombstone 仍是仅本机、最小且不可传播的控制信息；不会被误用为未来同步协议。

## 验证要求

- 为 SToken、URL、Windows 缓存、小助手、UIGF、Portable 和兼容 JSON 的 Tombstone
  重入建立 SQLite 反例；自动入口不得静默恢复。
- 覆盖 UIGF 补空 Revision、非空冲突、全量刷新不删除缺失事实和 OperationId 重试。
- 覆盖普通删除、永久删除、已无 current 的历史、Resolved／Unresolved 父范围 purge、
  跨档案共享 ChangeSet 失效和幸存档案不变。
- 覆盖显式重引入后 Undo 恢复原活动 Tombstone，以及 marker/version 变化时拒绝撤销。

