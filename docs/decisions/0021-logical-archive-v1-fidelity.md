# ADR 0021：跨端逻辑 Archive v1 的保真与本地状态边界

状态：Proposed

提议日期：2026-10-23

## 背景

Phase 8B 已提供 Gacha、角色／武器和挑战三个领域的独立 Portable，但它们不能直接组成完整备份：

- Gacha Portable v1 不携带 Revision，并拒绝 `Unresolved` 账号和缺少 UIGF 必需字段的记录；
- 角色／武器和挑战 Portable 携带领域事实及适用修订，但不携带本机 Undo、ChangeSet 或 Tombstone；
- 三个 exporter 分别读取生产库，不能证明来自同一个跨领域一致时点；
- 空档案、空账号、全局父对象、可移植设置和本机运行状态没有共同容器契约。

直接序列化 SQLite 行会把数据库实现变成公开格式；直接打包生产 SQLite 文件又会泄漏本机状态，并且不能作为 Windows／Android 的长期公开契约。Archive 因此必须使用独立逻辑模型，并明确哪些状态可以跨设备解释。

本 ADR 只提出 8C 首版语义。维护者接受前，不据此发布格式、恢复真实数据库或实现 Replace。

## 已接受且不重新讨论的前提

- 同一角色可存在于不同档案；业务数据按档案内 `GameAccount` 副本独立。
- 共享角色身份尽量稳定，但包内父对象引用不等于目标本地主键。
- 显示名称不参与身份；档案名称保持完整字符串精确匹配。
- Revision、Undo、操作历史和未来同步 Journal 是不同概念。
- `LocalOnlyTombstone` 只作用于本地，不作为远端删除传播。
- Chronicle 是可重建投影，不是事实源，也不进入逻辑 Archive。
- 凭据、设备路径和平台 URI 不进入普通 Archive。

## 提议决定

### 1. 产品名称与完整性声明

首版名称为“跨端逻辑 Archive”。它保留已支持范围内的逻辑事实和历史，不宣称：

- 覆盖账号在游戏中的全部历史；
- 原样镜像某次应用安装；
- 携带源设备的撤销资格、操作重试状态或缓存；
- 证明文件作者、来源真实性或新旧顺序。

Manifest 使用 `declared_scope` 和逐 Dataset 计数描述导出范围，不使用一个全局 `Complete` 推导更高层级完整性。

### 2. 逻辑父对象

一个容器可表达多个档案，并保留：

- 空档案；
- 没有领域事实的空账号；
- 已解析账号的规范自然身份和共享身份 GUID；
- `Unresolved` 账号的现有本地字段、占位状态和结构化未解析原因；
- 包内稳定 `archive_ref`、`account_ref` 和 `role_identity_ref`。

来源 `PlayerArchive.Id`、`GameAccount.Id` 只作为包内引用。恢复时目标数据库生成或映射自己的本地 ID；不同档案中的同一角色副本不能合并业务数据。

### 3. 事实与历史

Archive v1 保留下列逻辑内容：

- 当前 Gacha 事实及其有效来源、原时间偏移和已保留事实 Revision；
- 角色／武器不可变观察、手工获取当前事实及已保留来源修订；
- 挑战不可变观察历史；
- 上述事实所属的空父对象和稳定领域引用。

满足 UIGF 4.2 必需字段的当前 Gacha 事实继续以 UIGF 作为标准事实通道；Archive 补充载荷与其一一关联并保存 Furina 语义。无法由 UIGF 表达的当前事实和 Revision 使用版本化 `gacha-history` 逻辑载荷，记录结构化原因，不伪造 `item_id`、卡池类型、UID 或角色身份。

同一当前事实不得因同时存在于 UIGF 和补充载荷而计为两条。Revision 的 Portable 身份和内容可以保留，但来源设备的 `ChangeSetId`、本地版本令牌和撤销资格不得复制为目标并发令牌。

### 4. 本机运行状态

源设备以下状态不迁移：

- DataChangeSet、EntityChange、OperationHistory、UndoMaterial 和 OperationCommitResult；
- Portable／Domain Receipt 及其目标本地映射；
- LocalOnlyTombstone；
- RoleIdentityAlias；
- Chronicle scope epoch、generation、entry 和游标；
- UI 选择、窗口状态、Android SAF URI、设备权限、DeviceId、缓存和凭据。

目标恢复产生自己的 Receipt、ChangeSet、并发令牌、别名和 Chronicle generation。保留事实 Revision 不等于恢复源 Undo 栈。

### 5. Tombstone 与历史内容

来源 LocalOnlyTombstone 不进入 Archive。目标现有活动 Tombstone 在 Preview 和 Apply 时继续有效：

- 默认抑制对应当前事实；
- 也阻止通过 Revision before-image 或其他历史载荷恢复已在目标 purge 的具体内容；
- 只有绑定目标 marker/version 的显式重引入确认可以停用抑制；
- 重引入后的 Undo 继续遵守 ADR 0014。

Archive 不建立“未来所有未知记录永久禁止导入”的新范围标记。

### 6. Merge、Replace 与目标 Undo

`Merge` 默认只安全补充；同稳定身份同内容为 NoOp，不同非空事实或 Revision 内容为 Conflict，不能按遍历顺序覆盖。整个 Archive 使用一个 Preview 和一个跨领域事务。

建议的目标撤销边界是：

- 只向既有父对象新增事实、且现有领域规则可捕获完整材料时，整个跨领域操作可以成为一个可撤销 ChangeSet；
- 只要操作创建档案／账号／身份别名、应用来源历史修订、变更设置或存在任一不可撤销子项，整个操作不承诺可撤销；
- 不允许把一次 Archive 恢复拆成多个“看似可撤销”的独立领域操作。

`Replace` 不在 8C.0～8C.1 实现。建议它只替换用户明确选择的档案／账号／领域范围，不默认清空整库；它本身不可通过普通 Undo 恢复，提交前必须建立受生命周期约束的内部一致恢复点。目标活动 Tombstone、未选档案、凭据和设备设置不受影响。

### 7. 可移植设置

首版白名单仅包含已经有明确语义的设置：

- 全局 `RequireUniqueArchiveNames`；
- 每档案 `UndoLimit`，合法范围 0～1000。

导出保留来源值。恢复默认保留目标值，用户可在 Preview 中选择应用来源值；唯一名称冲突必须在提交前解决。当前实现尚未持久化可切换的全局唯一名称值，因此 8C.1 只能导出当前固定行为 `true`，不得假装读取了不存在的设置。

### 8. 容器、版本和未知内容

候选容器标识为 `furina-archive`，版本 `1.0`。Archive、各 Dataset、数据库 Schema 和 AppVersion 独立演进。

- 未知 future Archive major 拒绝；
- 未知必需 Dataset 拒绝；
- 未知可选 Dataset 只允许 Reader 检查并报告；恢复必须由用户明确接受“部分恢复”，且不得报告为完整成功；
- v1 是首版，不编造 v0 Migrator；
- 删除字段、改变身份／引用／历史语义提升 major。

`checksums.sha256` 是条目字节哈希的唯一权威清单，覆盖 Manifest 和全部逻辑载荷但不覆盖自身。Manifest 保存路径、类型、版本、必要性、范围、记录数和长度，不重复保存第二份哈希真相。SHA-256 只用于损坏检测。

### 9. 一致导出与原子恢复

一次导出必须来自同一个 SQLite 一致读取边界。建议使用 SQLite Online Backup 生成应用私有临时快照，再从快照调用逻辑 exporter；临时数据库不是 Archive 载荷，成功或失败后均由 session 清理。

未来恢复必须经过 `Inspect → Validate → Migrate → Plan → Preview → Apply → Verify`，并在一个跨领域事务中提交。依次调用三个各自提交的 Portable Apply 不满足原子恢复。

## 容器候选布局

```text
manifest.json
datasets/catalog.v1.json
datasets/gacha/<account_ref>/gacha.uigf.json
datasets/gacha/history.v1.ndjson
datasets/domain/character-weapon-history.v1.json
datasets/domain/challenge-history.v1.json
settings/portable.v1.json
checksums.sha256
```

没有对应事实时可以不写某个领域 Dataset；空档案和空账号始终由 catalog 保存。首版没有附件目录和凭据载荷。

## 影响

- 8C.1 需要新的 Services DTO／端口及 Infrastructure Codec，而不能复用 SQLite Row。
- Gacha Portable v1 保持不变；Archive 的 Gacha history 是新 Dataset，不静默扩展 v1。
- Schema 7 在纯导出阶段无需变更；恢复实现是否需要新表或迁移由 8C.2～8C.3 的事务设计决定。
- Archive Reader 可以在不打开数据库的情况下验证容器；8C.1 不提供生产 Apply。

## 需要维护者确认

1. 接受“逻辑 Archive”只迁移事实、来源、历史观察、已保留事实 Revision 和白名单设置，不迁移源 Undo／操作历史／Receipt／Tombstone／Chronicle。
2. 接受 UIGF 继续承载可标准化当前事实，Archive-native Gacha history 无损承载不完整事实、Unresolved 归属和 Revision。
3. 接受目标 Tombstone 同时阻止当前事实与会泄露已 purge 内容的历史载荷，除非显式重引入。
4. 接受上述整个 Archive 操作的保守撤销资格规则。
5. 接受设置白名单及“默认保留目标、Preview 可选择来源”的策略。
6. 接受 Replace 仅限明确范围、普通 Undo 不恢复 Replace，并要求提交前内部一致恢复点。
7. 接受 checksums 为唯一哈希真相，以及未知可选 Dataset 只能经明确部分恢复确认后跳过。

## 测试要求

- 最小包、完整包和包含 Unresolved／不完整 Gacha／Revision／空父对象的包均可在无数据库环境校验；
- 漏掉已有 Revision、改变时间精度、误合并同 UID 副本或删除空档案时，语义比较失败；
- future major、未知必需 Dataset、坏哈希、重复规范路径、路径穿越、截断和资源超限结构化拒绝；
- 多领域导出在并发写入下仍来自同一快照；
- Reader／Validator 不依赖 Chronicle、网络、MAUI 或生产数据库写入；
- 后续 Apply 的任一领域或 Receipt 故障必须使整个目标语义状态不变。
