# Archive v1 数据保真矩阵

状态：Accepted 8C.0 fidelity baseline；依赖 Accepted ADR 0021

本矩阵从 8C.0 冻结时的 Schema 7 持久化对象出发，说明逻辑 Archive 应保留的语义、目标端处理方式和排除原因。表名只用于审查当前覆盖面，不进入公开格式；Archive DTO 不得镜像 SQLite Row。8C.3 已将主库推进到 Schema 8；新增对象的处理见后文增量表。

## 结论

Archive v1 的“完整”限定为：

> 对导出范围内、当前应用已支持的领域，保留当前事实、必要来源、历史观察、仍保留的事实 Revision、空父对象和声明的可移植设置。

它不表示游戏全部历史、源安装位镜像或源本机 Undo。任何被选范围中无法按本矩阵无损表达的对象，都必须在导出前产生结构化阻塞诊断，不能静默跳过。

## Schema 7：22 张表

| # | 当前持久化对象 | 语义角色 | Archive v1 处理 | 目标端解释 |
|---|---|---|---|---|
| 1 | `PlayerArchives` | 用户档案父对象 | 保留 | 保存名称、源引用和时间；包括空档案。目标 ID 重新生成或映射 |
| 2 | `GameRoleIdentities` | 跨档案共享角色身份 | 按被导出账号引用保留 | 保留稳定 GUID 和规范自然身份；同 GUID 不同自然身份冲突 |
| 3 | `GameAccounts` | 档案内独立账号副本 | 保留 | 包括空账号、显示信息、占位状态及 Unresolved 诊断；目标本地 ID 重映射 |
| 4 | `GachaRecords` | 当前 Gacha 事实 | 全部保留 | 标准字段完整者由 UIGF 4.2＋一一关联补充项表达；其余由 Gacha history 表达 |
| 5 | `DataChangeSets` | 源设备操作提交边界 | 排除 | 目标恢复产生自己的跨领域 ChangeSet；源 ID 不成为目标操作身份 |
| 6 | `DataChangeSetArchives` | 源操作影响档案索引 | 排除 | 可由源操作历史理解，但不属于事实 Revision 本身 |
| 7 | `EntityChanges` | 源操作实体摘要 | 排除 | 目标按实际 Apply 重新生成；不能复制本地版本令牌 |
| 8 | `GachaRevisions` | 已保留 Gacha 事实演变 | 保留逻辑内容 | 保留稳定 Revision 引用、事实引用、before/after 和发生顺序；脱离源 ChangeSet/Undo |
| 9 | `OperationHistory` | 源设备撤销资格 | 排除 | 源撤销资格不跨设备；保留 Revision 不等于恢复 Undo |
| 10 | `UndoMaterials` | 源设备反向操作材料 | 排除 | 目标只为本次恢复按自身规则捕获材料 |
| 11 | `ArchiveUndoSettings` | 每档案撤销上限 | 白名单设置保留 | 以档案引用保存 0～1000；默认保留目标，Preview 可选择来源 |
| 12 | `OperationCommitResults` | 源 OperationId 重试结果 | 排除 | 目标恢复使用新的操作身份和 Receipt |
| 13 | `LocalOnlyTombstones` | 本地不可逆删除抑制 | 不传播 | 源 Tombstone 不迁移；目标活动 Tombstone 仍约束 current 与历史内容 |
| 14 | `RoleIdentityAliases` | 源数据库身份别名 | 排除 | 目标依据来源稳定身份与目标身份产生自己的别名 |
| 15 | `PortableImportReceipts` | Gacha Portable 本机接收证据 | 排除 | 不是来源事实；目标为 Archive 恢复生成新 Receipt |
| 16 | `PortableImportReceiptAccounts` | Receipt 本地账号映射 | 排除 | 来源本地账号 ID 不应进入目标映射 |
| 17 | `ArchiveDomainFacts` | 角色／武器／挑战当前事实或不可变观察 | 全部保留 | 解析为所属领域 DTO；不直接序列化 `SnapshotJson` Row |
| 18 | `ArchiveDomainRevisions` | 手工获取等领域 Revision | 保留逻辑内容 | 保留稳定 Revision 和 before/after；脱离源 ChangeSet/Undo |
| 19 | `DomainPortableReceipts` | 领域 Portable 本机接收证据 | 排除 | 目标 Archive 恢复生成统一 Receipt |
| 20 | `ChronicleScopeStates` | 可重建投影 epoch | 排除 | 恢复后由目标事实写入推进或重建 |
| 21 | `ChronicleGenerations` | 可重建读取快照 | 排除 | 不迁移 generation、过期时间或游标 |
| 22 | `ChronicleEntries` | 可重建 Chronicle 条目 | 排除 | 由目标领域事实和本地规则重新生成 |

## Schema 8 增量：4 张表

| # | 当前持久化对象 | 语义角色 | Archive v1 处理 | 目标端解释 |
|---|---|---|---|---|
| 23 | `PortableApplicationSettings` | 可移植应用设置的目标权威值 | 只通过 manifest 白名单设置表达 | 当前只承载 `RequireUniqueArchiveNames`；不得把 SQLite Row 直接写入包 |
| 24 | `ArchiveRestoreReceipts` | 目标本地恢复提交与幂等证据 | 排除 | 每次目标 Apply 生成自己的 Receipt；来源 Receipt 不迁移 |
| 25 | `ArchiveRestoreReceiptArchives` | Receipt 的来源→目标档案映射 | 排除 | 属于目标本地操作结果，不是来源档案事实 |
| 26 | `ArchiveRestoreReceiptAccounts` | Receipt 的来源→目标账号映射 | 排除 | 属于目标本地操作结果，不是来源账号事实 |

## 非表状态

| 对象 | 当前事实 | Archive v1 处理 |
|---|---|---|
| `RequireUniqueArchiveNames` | Accepted 默认启用；Schema 8 已有 `PortableApplicationSettings` 权威 Store | v1 字段保留；导出读取权威值，Merge 仅按确认后的白名单设置写入 |
| 当前页面选择 | MAUI Preferences 保存档案／账号选择 | 排除；它是页面本地状态，不改变业务事实 |
| Passport 账号和凭据 | SecureStorage／平台账户服务 | 排除；普通 Archive 不携带 Cookie、SToken、验证码、密钥 |
| 元数据和图标 | 内置卡池基线与可清理图标缓存 | 排除缓存；版本化公共元数据由应用提供，不作为私人 Archive 事实 |
| Android URI／Windows 路径 | 平台文件和窗口状态 | 排除 |
| 临时导入 staging | 应用私有会话文件 | 排除并在会话结束清理 |
| SQLite 主库／WAL／SHM | 当前存储实现 | 不作为公开载荷；仅允许内部一致快照读取 |
| 原始来源响应／附件 | 当前 Schema 8 没有通用 RawSnapshot／附件持久化 | v1 不编造；未来首次持久化前新增独立 Dataset 和保留策略 |

## 领域路径

### Gacha

| 内容 | 现有 Gacha Portable v1 | Archive v1 |
|---|---|---|
| 已解析账号、标准字段完整的当前事实 | 支持 | 保留，继续使用 UIGF 4.2 标准通道 |
| Unresolved 账号 | 拒绝整个选择范围 | 由 catalog 保留账号；事实由 Archive-native history 保存 |
| 缺 `item_id`／卡池类型等标准字段 | 拒绝整个选择范围 | 保存原字段和结构化不可标准化原因，不伪造 |
| 原时间偏移和来源 | supplement 保存 | 保留 |
| Gacha Revision | 不支持 | 保存逻辑 before/after 及稳定 Revision 引用 |
| Undo／ChangeSet／Receipt | 不支持 | 仍不迁移源状态；目标恢复另行生成 |

### 角色／武器

| 内容 | 现有领域 Portable v1 | Archive v1 |
|---|---|---|
| 全部不可变状态观察 | 支持 | 复用领域语义 |
| 手工获取当前事实 | 支持 | 复用领域语义 |
| 手工获取 Revision | 支持 | 复用领域语义 |
| 空账号／空档案 | 领域文件本身不能完整表达 | catalog 保留 |
| 本地 Undo／Tombstone／Receipt | 不迁移 | 不迁移源状态 |

### 挑战

| 内容 | 现有领域 Portable v1 | Archive v1 |
|---|---|---|
| 深境螺旋全部不可变观察 | 支持 | 复用领域语义 |
| 当前最佳证据 | 派生 | 不保存；目标重建 |
| 未支持挑战模式 | 受控拒绝 | 不伪装支持；遇到已持久化未知领域对象则阻塞完整导出 |
| 空账号／空档案 | 领域文件本身不能完整表达 | catalog 保留 |

## 保真反例

以下任一情况都不能标记为完整逻辑 Archive：

1. 当前数据库存在 Gacha Revision，但包只有当前 UIGF。
2. 选择范围存在 Unresolved 账号或不完整 Gacha，exporter 静默跳过。
3. 删除空档案或没有事实的账号，因为记录数为零。
4. 将两个档案中同一 UID 的账号事实合并。
5. 将源 `ChangeSetId`、目标本地版本或 Chronicle generation 当作跨端稳定身份。
6. 把 LocalOnlyTombstone 当作远端删除传播，或由历史载荷绕过目标 Tombstone。
7. 用当前时间、导出时间或导入时间填充未知业务发生时间。
8. 丢失原始时区偏移、Completeness 声明范围或来源端 provenance。
9. 忽略未知必需 Dataset 仍报告“完整恢复”。
10. 三个领域在不同读取时点导出，却宣称来自同一备份快照。

## 当前支持边界

- UIGF 官方当前列出的 Current 版本为 4.2；现有 Gacha Portable v1 继续固定输出 UIGF 4.2。
- Furina Archive v1、Gacha history Dataset 和容器布局已作为内部实施契约接受，但仍不是公开发布格式。
- Schema 7 基线足以完成只读逻辑导出；Schema 8 只增加目标权威设置和恢复本地状态，不扩大 Archive v1 的来源事实范围。8C.3 已实现无正式产品入口的内部原子 Merge。
- 现有角色／武器及挑战 Portable 虽已通过双端文件验收，仍为 internal / unpublished。
