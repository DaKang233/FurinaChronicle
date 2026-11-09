# ADR 0012：Gacha Portable v1 使用标准载荷与 Furina 补充载荷

状态：Accepted

日期：2026-10-06

## 背景

UIGF 4.2 能表达原神抽卡事实，但不能表达 FurinaChronicle 的档案内账号副本、共享角色身份、原始时间偏移和来源元数据。直接导出 SQLite Row 会把数据库实现固化为公开格式；只导出 UIGF 又不能完成 Furina 数据的语义迁移。

生产导入还必须遵守 `Inspect → Validate → Migrate → Plan → Preview → Apply → Verify`，其中 Apply 需要真实 `DataChangeSet`。Phase 8B.2 早于变更集与修订基础，不能以临时写入绕过该门槛。

## 决定

首个 Furina 机器可读领域格式为 `furina-gacha-portable` v1。它是一个单领域 ZIP 文件包，一次只表达一个源档案中选择的一个或多个已解析原神账号副本：

```text
manifest.json
accounts/<account_ref>/gacha.uigf.json
accounts/<account_ref>/supplement.ndjson
```

- 每个账号的标准事实使用独立 UIGF 4.2 文件；补充载荷只承载标准无法无损表达的账号身份、原始时间表示和来源信息。
- 标准记录与补充记录使用 `(account_ref, external_record_id)` 关联。补充记录不是第二条抽卡事实。
- `account_ref` 和源档案 GUID 只用于包内引用，不作为目标数据库主键。共享角色 GUID 按既有身份别名规则规划，不能代替档案内副本引用。
- 来源端 provenance 与目标设备本次接收上下文分开。读取和计划保留来源端声明，但不会把它视为目标设备重新认证的官方证据。
- 首版只支持已解析账号及能满足 UIGF 必需字段的记录。选择范围包含 `Unresolved` 账号或缺少必需字段时结构化拒绝，不静默跳过。
- 导出选择范围只说明本包包含哪些副本和记录，不声明账号全部历史完整，不使用 `Completeness.Complete`。
- Phase 8B.2 提供格式、校验、导出和只读 AddOnly 计划；生产 Apply、别名持久化和真实数据库重导入等待 8B.3/8B.4 的 DataChangeSet 事务基础。

## 原因

该结构使 UIGF 文件可以被其他兼容工具单独使用，同时让 FurinaChronicle 保留标准之外的必要语义。按账号分载荷避免共享角色 GUID 错误合并不同档案副本，也允许流式生成与验证。延后生产 Apply 可以保证首次真实写入就满足已接受的修订和原子事务要求。

## 后果

- Format v1、UIGF 版本、数据库 Schema 和应用版本独立演进。
- 完整 Archive、Revision、ChangeJournal、云同步和其他游戏不进入该格式。
- SHA-256 和长度只用于损坏与错配检测，不证明内容真实性。
- 未知 future major 在任何业务写入前拒绝；同一 major 的未知可选字段安全忽略，未知枚举返回结构化不支持错误，不提升为已知来源。
- 若以后需要携带 UIGF 无法表达的不完整事实，必须通过新版本明确扩展，不能复制 SQLite Row 或伪造标准字段。

## 迁移影响

v1 是首个实际 Furina Gacha Portable 格式，没有历史 v0 Migrator。版本分派仍必须存在；未来真实旧版本通过显式 Migrator 升级。

## 测试要求

- 不经过 SQLite 的领域/Portable/文件语义往返；
- 相同角色在不同档案中的包保持副本独立；
- 身份 GUID 别名与冲突只形成计划，不按遍历顺序决定；
- 原始时刻、偏移、亚秒和来源元数据保持；
- 孤立/重复/冲突补充项、哈希、长度、路径、截断、超限和 future major 均结构化失败；
- Plan/Preview 不创建档案、账号、别名或业务记录。
