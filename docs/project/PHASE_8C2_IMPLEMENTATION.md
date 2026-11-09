# Phase 8C.2 Archive 恢复规划实施记录

状态：Completed（planning only；no production apply）

日期：2026-10-23（Asia/Shanghai）

实施基线：`feat/phase-8 @ c0d758bc53a08b916f3cf5556ed7a0b161acf54a`

契约依据：[ADR 0021](../decisions/0021-logical-archive-v1-fidelity.md)、[Archive RestorePlan v1](../specifications/ARCHIVE_RESTORE_PLAN_V1.md)、[Archive v1 内部实施规范](../specifications/ARCHIVE_V1_PROPOSED.md)和[保真矩阵](../specifications/ARCHIVE_V1_FIDELITY_MATRIX.md)。

## 结论

8C.2 已完成 `Inspect → Validate → Stage → Plan → Preview model` 的工程基础。一个通过 Archive v1 Reader 验证的包可以在 app-private session 中形成私有有界索引，并与目标数据库的一致只读快照比较，生成不写生产库的统一 RestorePlan。

RestorePlan 覆盖当前支持的所有 Archive 领域及必要父对象，不是把三个 Portable Plan 拼接起来。它不提供 Apply、Receipt、Replace 或正式 UI，因此不能称为恢复闭环；这些仍属于 8C.3 及后续设备验收。

本轮没有修改 Schema 7，没有读取或写入真实生产数据库和凭据，没有停用 Tombstone，也没有把 source ChangeSetId 当成跨设备事实传播。

## 已实现的生命周期

```text
系统文件流
  → app-private 输入副本 + Archive SHA-256
  → 正式 Archive Reader 完整验证
  → app-private SQLite staging
  → 目标 SQLite Online Backup 一致快照
  → 映射、语义比较、Tombstone 和设置分析
  → 确定性 RestorePlan / Preview model
  → 取消或释放 session
```

- 非 seek 输入流可以正常 staging；来源流不被假定可重读。
- Reader 继续负责 future major、未知必需 Dataset、路径、哈希、长度、数量、父引用和资源上限验证。
- staging 保存全部当前事实和 Archive 中保留的 Revision；发生时间、来源、Completeness、偏移及其他可移植语义按正式 Codec 往返。
- 输入复制、验证或索引失败时只清理本 session；成功后不再保留重复的原 Archive 私有副本，staging session 在释放时删除。
- 目标一致快照使用 SQLite Online Backup；状态令牌覆盖备份数据库内容，记录数未变化但内容变化时旧 Plan 仍会失效。
- 目标当前事实和 Revision 按“账号 + 稳定 ID”查询，避免为一次 Preview 缓存账号的全部历史。

## 统一规划语义

### 档案与账号

- 档案只按完整字符串 Ordinal 精确匹配；trim 或大小写不同只形成相似候选，不自动选择。
- 多个精确同名目标要求显式决定；新建名称在唯一名称模式下使用稳定的 ` (2)`、` (3)` 编号。
- 已解析账号只按规范自然身份匹配；同 GUID 对应不同自然身份、来源档案内自然身份重复均为阻塞冲突。
- 同一自然身份在不同档案仍形成独立业务副本；不会跨档案合并事实。
- Unresolved 在新档案可以保持未解析创建；映射到已有档案时不按 UID、显示名或占位数据猜测。

### 事实与 Revision

- 不存在的稳定 ID 为 `Add`，相同 ID 且语义摘要相同为 `Skip`，相同 ID 但可移植语义不同为 `Conflict`。
- 当前事实覆盖 Gacha、角色／武器观察、手工获取和挑战观察；Revision 覆盖 Gacha 与手工获取的已保留历史。
- Gacha 标准事实和 standard extension 已由 Reader 合成一个逻辑事实，不重复计数。
- Archive 中的 Gacha／手工 Revision 不携带来源 ChangeSetId；目标 Apply 将来必须创建自己的 ChangeSet／Receipt。

### Tombstone、设置与 Undo

- 当前事实及会泄露已 purge 内容的 Revision 都形成 Tombstone probe。
- 命中活动 Tombstone 时，确认项绑定事实引用、marker key 和 version；8C.2 只报告，不停用 marker。
- 已有档案默认保留目标设置；新建档案采用来源 `UndoLimit`；请求应用来源设置时列出实际差异。
- 只有既有父对象上的纯当前事实新增，且没有 Revision、设置变化、身份别名或 Tombstone 重引入时，整个 Plan 才能标记为普通 Undo 候选。
- 空间数值是规划估算，不是写库承诺；8C.3 Apply 仍须读取实时容量并执行既有清理确认规则。

## Plan 绑定与失效

每个 Plan 绑定以下内容并计算确定性指纹：

- 包 SHA-256、Archive 版本和 partial 状态；
- 目标 Schema 7、完整状态令牌和快照长度；
- Merge 模式、档案／账号映射和稳定的新建 GUID；
- 每条事实／Revision 的 Add、Skip、Conflict 或 Reintroduce 分类；
- Tombstone marker/version、设置政策和全部显式确认；
- staging、预计数据库增长、预计 Undo 材料和整体 Undo 资格。

相同来源、目标快照和决定重复规划得到相同指纹。目标状态令牌或 Schema 变化、包指纹不一致、映射未解决、阻塞冲突或确认不足时，Plan 不可 Apply。8C.2 只提供新鲜度检查，不执行 Apply。

## 主要实现位置

- `FurinaChronicle.Services/Archives/Portable/ArchiveRestoreModels.cs`：staging、目标快照、映射、事实动作、冲突、确认、设置、资源和 Preview 合同。
- `FurinaChronicle.Services/Archives/Portable/PlanArchiveRestore*.cs`：统一 Merge 规划、确定性 ID／指纹、策略和 Preview。
- `FurinaChronicle.Services/Archives/Portable/ArchiveRestoreSemanticDigest.cs`：跨 adapter 共用的可移植语义摘要。
- `FurinaChronicle.Infrastructure/Archives/Portable/SqliteArchiveRestoreInputStager.cs`：输入复制、正式验证、私有 SQLite 索引和 session 清理。
- `FurinaChronicle.Infrastructure/Archives/Portable/SqliteArchiveExportSnapshotFactory.cs`：目标一致快照、状态令牌、有界事实查询和 Tombstone 查询。
- `FurinaChronicle.App/Composition/ArchiveServiceCollectionExtensions.cs`：8C.2 服务注册；未增加产品入口。
- `FurinaChronicle.Tests/Infrastructure/Archives/Portable/` 与 `FurinaChronicle.Tests/Services/Archives/Portable/`：staging、真实 SQLite 目标快照和规划器自动化。

## 本地提交

| 提交 | 内容 |
|---|---|
| `588165f` | 固化 8C.2 RestorePlan 契约与版本里程碑 |
| `b0a4cc7` | 建立恢复规划合同和 Preview 模型 |
| `3c05e94` | 排除 Archive Revision 中的 source ChangeSetId |
| `7773ca0` | 实现正式验证后的私有 staging |
| `e2e193d` | 绑定目标一致快照和状态令牌 |
| `a0c5225` | 实现统一 Merge RestorePlan |
| `8c458d1` | 注册 staging、target snapshot 和 planner 服务 |
| `4125fda` | 将目标事实查找改为按稳定 ID 有界查询 |

## 自动化与构建证据

| 项目 | 结果 |
|---|---|
| Release 全量测试 | 559/559 passed，0 failed，0 skipped |
| Planner 定向测试 | 8/8 passed |
| MAUI Windows Release | passed，0 warnings，0 errors |
| 独立 WinUI Release | passed，0 warnings，0 errors |
| Android Release | passed，0 warnings，0 errors；SDK 为 `E:\AndroidSDK` |
| Schema | 仍为 7；无迁移 |
| CI | Not run；本轮仅本地提交，尚未推送 |
| Windows／Android 文件交互 | Not run；8C.2 无正式入口 |
| 真实生产库 Preview／Apply | Not run；测试只使用合成数据和临时数据库 |

自动化覆盖全领域 staging、不可 seek 输入、失败清理、来源时间／来源元数据、精确／相似名称、自然身份冲突、Unresolved 不猜测、同 ID 异内容、Tombstone marker/version、设置和 Undo 资格、新父对象、Plan 确定性，以及目标内容变化但计数不变时的失效。

## 8C.3 最短交接

1. 实现 `IArchiveAtomicRestorer`，但只接受仍通过包指纹、目标状态令牌、Schema、映射、确认和 marker/version 重验的 Plan。
2. 复用现有 Store 规则并提取事务内 mutation helper；不要从协调器依次调用三个公开 Apply。
3. 在一个 SQLite 事务中提交父对象、全部当前事实、保留 Revision、设置、Tombstone 重引入、目标 ChangeSet／Receipt 和必要 Undo 材料。
4. 空间不足、取消、任意领域写入失败、Revision 失败和 Receipt 失败必须整体回滚；失败后 Plan/session 的清理状态需可解释。
5. 先关闭 Merge 的幂等与故障注入测试，再考虑 Replace。正式 UI 和真实 Windows↔Android 文件闭环仍需后续单独验收。
