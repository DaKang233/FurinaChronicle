# 待确认事项

状态：当前没有阻塞 Phase 8B.0 的产品方向确认项；工作包契约尚须按实施时点冻结

## 当前确认项与实施边界

维护者已确认核心产品定位及四项原则：领域自治、数据优先、开放导出、Chronicle 派生，见 [ADR 0011](../decisions/0011-domain-first-product-scope.md)。无需重开 Q01～Q06，也不把整个 Chronicle 或未来领域 Schema 作为共享基础的前置条件。

8B.0 只处理影响已存数据无损解释、跨端实体身份、公开格式兼容或不可逆数据安全的最小共享契约。已接受语义下、不落库且不发布格式的 Core 类型和纯规则可以推进；文档更新不代表数据库迁移、Schema 或全部契约已经验证完成。

[身份模型](../architecture/IDENTITY_MODEL.md) 已由维护者接受。档案内副本、共享身份、自然身份组成和导入重映射规则不再开放；规范 `GameBiz`/Server 编码、固定命名空间 UUID v5、旧占位账号 `Unresolved` 迁移以及共享 `Confidence`/`Completeness`/时间精度已于 2026-10-05 确认。

当前 Schema 8 及后续版本不提供数据库降级。项目目前尚未公开发布，只能保证实施未来版本保护之后的客户端遇到更高 Schema 时拒绝写入，不能追溯保证此前内部客户端的数据库行为。不得为了回开旧版而删除新表或重建数据库；发布升级前必须制作 SQLite 一致备份，不能在 WAL 活动时只复制主文件。

8B.4 的刷新抑制、UIGF 补空／冲突、新父实体导入 Undo 边界、人工纠正来源、
父实体删除范围、Unresolved purge 以及重引入 Undo 已由维护者确认，并记录在
[ADR 0014](../decisions/0014-phase-8b4-gacha-production-semantics.md)。这些项目不再是
本阶段开放问题；实现不得静默扩大为全局封禁、通用覆盖导入或父实体通用 Undo。

## 8B.0 保留的最小共享门槛

- GameRoleIdentity 与档案内副本关系、自然身份和导入重映射，副本互不联动；
- 可修改事实的稳定身份原则：内容修改不变成新实体，内容判重不代替身份；
- 基础时间、来源、置信度、完整性及其声明范围，未知信息不伪造；
- AddOnly/Upsert/Revision、撤销和删除的基本语义及事务安全；
- Portable 版本、兼容和引用原则，标准优先，不依赖 SQLite 或 Chronicle；
- 未知/未来数据库版本或 application ID 不匹配时保留原库、停止写入；仅新空库初始化。

具体退出条件和门槛由 [Phase 8B 实施计划](PHASE_8B_PLAN.md) 定义。未知版本保护是新增 Schema 前的硬要求，不能由阶段末验收替代。

## 下放到对应工作包的契约与待确认项

以下是尚未闭合的实施问题，不是整个 8B.0 的产品方向 blocker。必须在首次影响持久化/公开格式或对应产品行为前闭合；fixture 无法给出可靠答案、或选择会改变 Accepted 用户语义时，提交维护者确认，不由 Agent 静默决定。

| 项目 | 责任工作包 / 冻结时点 | 尚需明确 |
|---|---|---|
| 首个领域 Dataset | 8B.2 / 发布门槛 | ADR 0012、Codec、Schema、示例、兼容测试、真实数据库 A→B→再导出 fixture、Windows/Android Release 编译及维护者双端真实文件流程均已完成；格式仍未公开发布 |
| Revision/OperationHistory | 8B.3，持久化前 | 已由 ADR 0013 与 Phase 8B.3 实施契约闭合；后续只在引入外部附件或 Revision 清理策略时新增决策 |
| 手工角色/武器获取 | 8B.5，保存/Portable 前 | ADR 0016 已接受：稳定 AcquisitionId、相似仅提示、Week 使用周一 UTC+8 `[start,end)`、迁移来源修订而不迁移本机 Undo |
| 挑战观察 | 8B.5，首批模式保存/导出前 | ADR 0017 已接受：稳定观察引用、相邻内容判重、可信顺序保留 A→B→A、迟到不倒退当前视图 |
| 挑战完成 | 8B.6 | ADR 0019 已接受：首版生产仅深境螺旋，版本化目标末关为正面证据，完成与满星分离，缺证据保持 Unknown |
| 获取序号和五星间隔 | 8B.6 | ADR 0018 已接受：必须标明 Known／Unknown／Ambiguous 与适用范围；无覆盖证明不宣称真实首次、满命或 ExactPity |
| Chronicle 子系统 | 8B.6 | ADR 0018 已接受：本地稳定 EntryId、Undated、有界 generation、严格游标绑定，以及纠正／Undo／删除后的显式失效 |
| 完整 Archive | 8C | ADR 0021 的逻辑范围和 ADR 0022 的 Merge／Receipt／设置语义已接受；8C.1～8C.3 已完成。8C.4 的分层 Replace、父实体删除、旧 Undo 失效和单恢复点生命周期由 Proposed ADR 0023 等待维护者确认 |
| 重要成就元数据 | 成就及其 Chronicle 节点实施前 | 版本、分类键、来源及实际目录；8B 仅需虚构过滤 fixture |

Week 已由 ADR 0016 固定为周一 UTC+8 `[start,end)`；Chronicle 分页失效、获取证据退化和首批深境螺旋完成条件已由 ADR 0018／0019 闭合。未实施挑战模式、真实重要成就目录和完整 Archive 仍按各自实施时点冻结。

## Phase 8C.0 已确认决定

维护者于 2026-10-23 接受 ADR 0021 的以下决定。身份、名称、Revision 基本语义、LocalOnlyTombstone 本地范围和 Chronicle 派生属性继续沿用既有 Accepted 决定，不重新开放。

1. 逻辑 Archive 只迁移事实、来源、历史观察、已保留事实 Revision、空父对象和白名单设置，排除源 Undo／ChangeSet／Receipt／Tombstone／Chronicle。
2. Gacha 继续由 UIGF 4.2 承载可标准化当前事实，并以 Archive-native history 无损承载 Unresolved 归属、不完整事实和 Revision。
3. 目标活动 Tombstone 同时阻止当前事实和会泄露已 purge 内容的 Revision/history，直到用户进行绑定 marker/version 的显式重引入。
4. Archive Merge 采用整体保守撤销资格：任一父实体创建、来源历史导入、设置变更或不可撤销子项都会使整个操作不承诺可撤销。
5. 可移植设置首版只包含 `RequireUniqueArchiveNames` 和每档案 `UndoLimit`，默认保留目标值，并允许用户在 Preview 中选择来源值。
6. Replace 只作用于用户明确选择的范围，不进入普通 Undo，并要求提交前建立受生命周期约束的内部一致恢复点。
7. `checksums.sha256` 是条目哈希唯一真相；未知可选 Dataset 只能在明确接受部分恢复后跳过，且不得报告为完整成功。

正反例见 [ADR 0021](../decisions/0021-logical-archive-v1-fidelity.md) 和 [Archive v1 示例](../specifications/examples/ARCHIVE_V1_PROPOSED_EXAMPLES.md)。8C.1 可以按已接受契约固化导出格式的内部首版实现；格式仍未公开发布。

## 已确认事项

Q01～Q06 已由维护者确认：

- Q01：GameRoleIdentityId 尽量保持全局稳定。
- Q02：撤销次数按档案计数。
- Q03：档案名称使用包含首尾空白在内的精确匹配；模糊匹配只用于导入前提示。
- Q04：不提供“隐私删除”概念；提供只影响本地可管理数据的不可逆删除。
- Q05：取消无限撤销，单档案上限 1000 次，并允许完全关闭撤销功能。
- Q06：接受 ADR 0009 的 Chronicle 派生投影方案，并采用先共享基础、再提前角色/武器和挑战窄纵切的方向。

这些决定继续有效。本轮只按 ADR 0011 调整工作包作用域及先后：领域先独立成立，Chronicle 后消费，不改变 ADR 0009、0010 的产品行为。

## 后期事项

以下内容不阻塞 Phase 8B，在对应可选功能开始前另行决定：

- 完整 Sync/ChangeJournal、设备游标、分布式冲突和云同步威胁模型、密钥恢复；
- 社区统计具体上传粒度和撤回规则；
- 公共元数据更新清单是否必须签名，以及不同来源的逐字段合并规则；
- 不可逆删除 Tombstone 的长期压缩策略；
- Android 游玩时长采集是否值得请求额外系统权限。
