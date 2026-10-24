# ADR 0023：Archive 明确范围 Replace 与内部恢复点

状态：Proposed

提议日期：2026-10-24

## 背景

ADR 0021 已接受 Replace 只作用于用户明确选择的范围、不能通过普通 Undo 恢复，并要求
在提交前建立受生命周期约束的一致恢复点。8C.3 已完成 Add-only Merge，但其 Plan 只会
把差异分类为 Add／Skip／Conflict，不能表达目标独有事实、父实体删除或历史替换。

Replace 会删除目标当前事实和已保留历史，也可能使此前的 Undo 材料不再与当前事实兼容。
这些语义不能由实现顺序、UI 默认值或“全量”复选框静默决定。

## 推荐决定

### 1. 选择范围是显式、分层且不可重叠的

首版提供三种范围：

- `Archive`：一个已经映射的来源档案及其全部已支持逻辑内容；
- `Account`：一个已经映射的来源账号及其全部已支持逻辑领域；
- `Domain`：一个已经映射的来源账号下一个明确领域。

领域固定为 `Gacha`、`ItemObservation`、`ManualAcquisition` 和
`ChallengeObservation`。Gacha 与 ManualAcquisition 的 Revision 随其领域一起替换。

同一个 Plan 中不得同时选择祖先和后代范围，也不得重复选择同一范围。空选择不是
Replace；重新选择范围必须重新 Plan。Archive v1 没有的未来领域不因 Archive 级选择被
删除。

### 2. 每个选择范围重建为来源的精确逻辑状态

- Domain 范围只替换该账号对应领域的当前事实和适用 Revision。
- Account 范围替换全部四个领域及账号的可移植父属性；目标本地 AccountId 和已验证的
  共享身份映射保持不变。
- Archive 范围替换全部来源账号、四个领域、适用 Revision 和档案可移植父属性。来源中
  不存在的目标账号是目标独有父实体，必须在 Preview 中逐个列出并随该 Archive 范围
  删除。

映射后的目标 ArchiveId／AccountId 仍是目标本地身份。共享 `GameRoleIdentity` 行不因
最后一个账号副本被移除而自动删除；该清理不是 Replace 正确性的组成部分。凭据、Passport
关联、设备设置和未选档案不在范围内。

来源范围为空是有效的清空语义，但必须仍有一个明确的 Archive／Account／Domain 选择和
破坏性确认。不得把“包中没有此 Dataset”误解释为清空；只有 Manifest 声明该领域属于
导出范围且 Reader 已确认完整时才能精确替换。

### 3. Tombstone 和设置不被范围选择隐式覆盖

目标活动 `LocalOnlyTombstone` 始终保留。来源当前事实或 Revision 命中目标 Tombstone
时继续使用绑定 marker/version 的显式重引入确认；Replace 不创建新 Tombstone，也不因
清空范围停用现有 Tombstone。

`RequireUniqueArchiveNames` 和 `UndoLimit` 继续是独立的设置选择。选择 Archive 或
Account Replace 不等于选择应用来源设置。

### 4. Replace 和旧 Undo 的关系

Replace 自身始终不可通过普通 Undo 恢复，并以独立操作种类记录。

只要既有可撤销 ChangeSet 的任一 UndoMaterial 或 EntityChange 命中被替换父实体／事实，
该 ChangeSet 的整体 Undo 资格在同一事务中失效，原因记为
`SupersededByArchiveReplace`，其 UndoMaterial 删除。跨档案 ChangeSet 不拆分：即使只有
一个档案被 Replace，整个旧操作也不再可 Undo，但未选档案的业务事实保持不变。

历史 ChangeSet、Receipt 和不含事实内容的操作摘要可以保留；不得通过删除操作记录来伪装
“从未发生过”。

### 5. 提交前恢复点是目标本机状态

Replace 在第一次目标 mutation 前、持有同一数据库写边界时，以 SQLite Online Backup
在应用私有目录建立完整数据库恢复点，记录：

- RecoveryPointId 和 OperationId；
- 数据库 application id、Schema、长度和 SHA-256；
- Package／Plan 指纹；
- 创建和 Replace 提交时间；
- 仅含文件名的本地定位信息和状态。

恢复点不是 Furina Archive、普通 Undo 或跨端数据，不上传、不导出，也不进入 Receipt
映射。可用空间必须至少覆盖当前数据库一致副本、预计增长和固定安全余量；无法可靠建立并
校验恢复点时 Replace 不开始写入。

### 6. 生命周期有界且先保新后删旧

每个主数据库最多保留一个最近成功 Replace 的恢复点：

1. 新 Replace 先创建并校验候选恢复点；
2. Replace 事务提交后，候选转为当前恢复点；
3. 只有新恢复点与 Receipt 均已持久化，旧恢复点才标记过期并在提交后清理；
4. 失败／取消删除本次候选；进程终止留下的无元数据孤儿在下次启动清理；
5. 文件清理失败只留下可识别的过期文件，不回滚已经提交的业务事实。

因此存储峰值可能短暂包含旧恢复点、新恢复点和主数据库，容量检查必须按峰值而不是最终
保留量计算。8C.4 不提供无限保留、用户命名快照或多版本数据库时间机器。

### 7. 中断与查询边界

Replace 继续使用单一 SQLite 事务，不采用热 `File.Move` 或主库文件切换。提交前进程终止
由 SQLite 回滚；提交后以持久化 Archive Receipt 判断已提交并阻止重复执行。恢复点元数据
必须能够在重启后查询和校验。

8C.4 只实现恢复点创建、验证、枚举和有界清理，不在数据库连接打开时自动把恢复点换回
主库。未来如提供“恢复这个恢复点”，必须设计关闭连接、WAL／SHM、同卷原子切换、失败回滚
和启动恢复协议，不能复用普通 Undo。

## 推荐原因

- 分层明确选择既能表达真正的清空，也避免默认清空整库。
- 精确逻辑状态使 Replace 与 Merge 的“只补充”语义保持清晰分离。
- 整体失效旧跨档案 Undo 比尝试切割历史 ChangeSet 更安全，也不会修改未选业务事实。
- 单个本机恢复点提供事故兜底，同时让磁盘占用有确定上界。
- 不在 8C.4 做热文件切换，避免把 SQLite 连接和 WAL 生命周期风险混入业务事务。

## 反例

1. 只勾选某个账号的 Gacha，不得删除它的角色／武器观察或挑战记录。
2. Replace 档案 A 时，与 A 共享角色身份的档案 B 事实、父实体和 Tombstone 不变。
3. 来源空 Gacha Dataset 且范围明确包含 Gacha，可以清空目标 Gacha；缺失 Dataset
   不能被当作空 Dataset。
4. 目标 Tombstone 命中来源 Revision before-image 时，不能先清空再偷偷写回历史。
5. 跨 A/B 的旧可撤销 ChangeSet 命中 A 的 Replace 范围后整体不可撤销，但 B 的事实不删。
6. 创建候选恢复点后、Replace 提交前故障，主库不变且候选最终清理。
7. Replace 已提交而 UI 失败时，Receipt 和恢复点仍能在重启后查明，不能再次执行。

## 数据库影响

若本 ADR 被接受，8C.4 建议使用 Schema 9：

- 新增 `ArchiveReplaceRecoveryPoints`；
- Archive Restore Receipt 增加模式／RecoveryPoint 关联，或以 OperationId 与恢复点关联；
- 放宽 Archive Receipt 账号映射对目标账号的强外键，使已提交映射可以在显式 Archive
  Replace 删除目标独有账号后继续作为本地审计记录；
- 新增 Replace 操作种类和 `SupersededByArchiveReplace` Undo 失效原因。

迁移必须保留 Schema 8 所有 Receipt、映射、设置和事实，不推断历史恢复点。未来 Schema、
错误 application id、损坏库和迁移失败继续保留原库并拒绝写入。

## 需要维护者确认

1. 是否接受三层且不可重叠的 Replace 范围。
2. 是否接受 Archive 范围删除来源不存在的目标独有账号，而 Account／Domain 范围保留父
   实体。
3. 是否接受 Replace 精确替换已选当前事实和来源 Revision，但保留目标 Tombstone，并将
   设置作为独立选择。
4. 是否接受命中范围的旧跨档案 ChangeSet 整体失去 Undo 资格。
5. 是否接受每个数据库只保留一个最近成功 Replace 恢复点，以及先保新后删旧的峰值策略。
6. 是否接受 8C.4 不提供热主库回切，只完成恢复点创建、验证、查询和清理。
