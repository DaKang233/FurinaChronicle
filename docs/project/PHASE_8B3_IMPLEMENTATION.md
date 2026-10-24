# Phase 8B.3 实施契约

状态：Accepted implementation contract

实施状态：Completed（2026-10-06）

确认日期：2026-10-06

基线：`feat/phase-8`，起始提交 `7c2f969`

## 目标

为当前 Gacha 领域建立最小 DataChangeSet、Revision、有限 OperationHistory、不可逆本地删除和 Portable AddOnly 原子 Apply。共享层只定义操作范围与生命周期；首个事实快照、比较和恢复载荷由 Gacha 定义。

## 冻结模型

| 概念 | 首版含义 |
|---|---|
| OperationId | 一次提交请求的稳定 GUID；重试必须复用 |
| DataChangeSet | 操作种类、涉及档案、时间、来源、摘要、影响数量和可选 UndoOf |
| GachaFactReference | `GameAccountId + ExternalRecordId`；不使用 SQLite 自增 ID |
| FactVersion | 当前事实的单调递增本地令牌；任意实际落库变化推进 |
| GachaRevision | 版本 1 私有快照、前后版本、ChangeSet 和变化分类 |
| UndoMaterial | 恢复 Insert/Update/Delete 所需的小型版本化快照 |
| OperationHistory | 按档案索引的撤销资格与失效原因；跨档案不复制 ChangeSet |
| LocalOnlyTombstone | 稳定范围哈希、版本、活动状态、删除/重引入 ChangeSet；不含抽卡内容 |
| RoleIdentityAlias | 来源稳定身份到目标身份的单跳映射；必须自然身份一致 |
| PortableImportReceipt | 本机接收时间、批次、包指纹、映射和 ChangeSet；不覆盖源 provenance |

旧 Schema 行是“此前历史不可回溯的当前基线”。迁移只建立版本令牌，不生成 ChangeSet、Revision、撤销材料或历史时间。

## 变化分类

比较 Merge Policy 计算后的最终保存状态：

- 物品、卡池、星级、数量、发生时刻/偏移、`Origin` 或 `ImportedAt` 变化：`Substantive`，生成 Revision；可撤销操作保存材料。
- 只有 `FetchedAt` 或 `AcquisitionBatchId` 变化：`AcquisitionMetadataOnly`，更新 current row 与版本令牌，不生成 Revision、不占槽。
- 全部支持字段相同：`NoOp`，不生成 ChangeSet、Receipt、Revision 或撤销项。
- null、空字符串、UTC ticks 和原偏移按实际保存值精确比较，不把输入中会被 Merge Policy 保留的 null 误报为清空。

## 撤销与容量

- 只撤销所选档案最新的可撤销 ChangeSet；所有实体必须仍处于该操作的 after-version 和 after-content。
- Insert 撤销为删除、Update 撤销为恢复 before-image、普通 Delete 撤销为恢复原副本。
- 反向 ChangeSet记录 `UndoOfChangeSetId`，不可再次撤销；不提供 Redo。
- 跨档案任一档案的限额为 0 时，新跨档案操作整体不可撤销；任一档案淘汰共享项时，各档案索引同时失效。
- 默认 3；0 保留既有；1～1000 调低后立即淘汰最旧专用材料，Revision 保留；1001 拒绝。
- 存储不足提前清理返回绑定候选操作、档案、估算字节和状态令牌的确认计划。确认前、拒绝后或重验失败时无修改。

## 不可逆删除

Gacha Tombstone 键为规范字符串 `gacha|archive_id|role_identity_id|external_record_id` 的 SHA-256，小写十六进制。表中不保存外部记录 ID、物品、时间或 provenance。不可逆删除清除当前行、该事实的 Revision/Undo 快照，并使所有涉及它的历史项整体失效。

自动入口遇活动 Tombstone 返回抑制结果。显式重引入确认绑定 Tombstone ID、版本和目标；成功后保留非活动 Tombstone，并记录重引入 ChangeSet。该行为只影响本地数据库，不触及导出文件、其他设备或远端副本。

## Portable AddOnly Apply

- Apply 输入包含只读包、计划、输入 SHA-256、OperationId 和确认映射；调用前重新计算包语义指纹。
- 事务内重验精确档案名、目标 UpdatedAt、账号自然身份/版本、别名唯一性及涉及记录的实际内容。
- 任何 Conflict、计划过期、Tombstone 抑制或第二账号失败使整包回滚。
- 新档案使用计划中的本地 GUID并精确保存名称；新账号使用计划本地 GUID；来源 GUID 仅作为引用。
- 源 provenance 原样保存；Receipt 使用 `FurinaImport`、实际提交时间、新本地批次和包/映射引用。
- AddOnly 只插入缺失事实；相同事实 Skip；不同事实 Conflict，不调用旧 PreserveExisting 偷换语义。
- 相同 OperationId 返回原结果；新的重复请求若无任何持久化变化返回 NoOp，不占撤销槽。

## 反例 fixture

1. 计划后目标记录数量不变但内容改变；
2. 同事实仅批次/抓取时间变化；
3. 新路径 A→B 后旧入口 B→A，再请求 Undo；
4. 一个跨档案 ChangeSet 在另一档案被容量淘汰；
5. UndoLimit=0 后发生覆盖；
6. 不可逆删除后旧 Undo 试图恢复；
7. Portable 第二账号写入或别名失败；
8. 提交成功后 Verify/UI 失败，再以 OperationId 重试。

## 旧入口边界

8B.3 只提供 Gacha 内部服务/测试纵切和 Portable AddOnly 服务。现有 UIGF、刷新、手工纠正、账号/档案删除 UI 尚未全面接管；低层 Gacha Save/Delete 必须维护当前版本令牌，因而会安全阻止过期 Undo。Tombstone 抑制未接入的产品入口不得暴露不可逆删除功能。

完整旧入口适配、Portable UI、真实大包受控暂存读取和产品级删除流程留给 8B.4。

## 实施结果

Schema 5 已在单一事务迁移中完成。v1、v2、v3、v4 均通过显式路径升级；旧 Gacha 行以 `Version = 1` 作为当前基线，不生成虚构的 ChangeSet、Revision 或历史时间。新增持久化范围包括：

- `DataChangeSets`、`DataChangeSetArchives`、`EntityChanges`；
- `GachaRevisions`、`OperationHistory`、`UndoMaterials`、`ArchiveUndoSettings`；
- `OperationCommitResults`、`LocalOnlyTombstones`；
- `RoleIdentityAliases`、`PortableImportReceipts` 及账号映射。

已实现并验证：

- Gacha Insert/Update/Delete 的原子 ChangeSet、私有版本 1 快照和 OperationId 幂等；
- 纯抓取时间/批次变化只推进 current version，不制造业务 Revision；
- Insert/Update/Delete 反向 ChangeSet、版本前置条件、A→B→A 冲突和跨档案整体撤销；
- 每档案默认 3、0 保留旧材料并停止新捕获、1～1000、调低立即淘汰专用材料但不删 Revision；
- 容量已知且不足时返回绑定候选与目标的清理确认；确认前无修改，确认后在同一事务重验；
- 不可逆删除清除当前事实及可恢复快照，整项失去撤销资格；自动重引入受抑制，显式确认后 Tombstone 转为非活动；
- Portable AddOnly 在实际 SQLite 中原子创建档案、共享身份、账号副本、别名、Gacha 事实、接收收据和 ChangeSet；
- Portable 计划通过语义 SHA-256 绑定输入，并在事务中重验档案、账号、身份、数量和记录内容；纯重复新请求为 NoOp；
- 数据库 A 导出 → 数据库 B Apply → 再导出保持当前 Portable v1 支持字段的语义。

验证结果：

- `dotnet test` Release：421/421 通过；
- Windows Release 构建：通过；
- Android Release 构建（`E:\AndroidSDK`）：通过；
- `git diff --check`：通过。

以上是自动化与平台编译证据，不替代 Windows/Android 实机 Portable 文件选择、跨设备互传或真实大包运行验收。

## 8B.4 交接

- 现有 UIGF、SToken/URL/缓存刷新和手工入口仍调用低层 `SaveBatchAsync`；它们会推进版本令牌，但尚未生成完整 ChangeSet/Revision。
- 现有账号/档案删除 UI 尚未接管可撤销或不可逆命令；不可逆删除内部端口不得在 Tombstone 抑制覆盖全部自动入口前暴露。
- Portable AddOnly 已有稳定服务入口和真实 SQLite Apply，但尚无选文件→预览→确认 UI。
- 当前 Reader 为生成 Plan 仍在既定资源上限内物化整个模型；尚不能宣称 2,000,000 条记录的端到端有界内存 Apply。
- 新建档案或账号的 Portable 操作因父实体恢复尚未产品化而不进入撤销栈；导入既有账号的纯记录新增可以安全撤销。
