# Phase 8B 实施计划

状态：Accepted target

阶段状态：In progress（Phase 8A 已于 2026-10-04 关闭；8B.0、8B.1 与 8B.2 实现工作包已完成，下一工作包为 8B.3）

维护者已确认 ADR 0009、0010 和 Q06。本计划按 [ADR 0011](../decisions/0011-domain-first-product-scope.md) 收敛：最小共享基础先行，领域先独立保存、查询和导出，Chronicle 后消费。不要求一次冻结所有未来领域。

## 阶段目标

建立足以承载原神多个独立历史数据领域的共享基础，并用不同类型的数据领域证明这些领域能够独立保存、查询、导出和参与 Chronicle。共享身份、可移植性和领域基础保留未来按实际需求接入其他游戏的空间，但本阶段不以未实施游戏为验收对象。

产品定位是“本地优先的原神个人历史档案工具”。本阶段不建设通用历史数据平台，不追求完整的新页面、全部官方接口、完整 Archive 容器或自动同步协议。

## 工作包 8B.0：最小共享契约

实施状态：Completed（2026-10-05）

仅当决定属于以下类型时，作为实现前必须冻结的共享门槛：

1. 后续改变会使已保存数据无法无损解释；
2. 后续改变会改变跨端导入后的实体身份；
3. 后续改变会破坏公开可移植格式兼容；
4. 当前不明确会产生数据损坏或不可逆迁移风险。

最小契约包括：

- GameRoleIdentity 与档案内 GameAccount 副本关系、自然身份、导入重映射和独立副本范围；
- 可修改领域事实具有不随内容修改而改变的稳定身份；实体身份与内容判重分开，不在此统一发明所有领域的键算法；
- 发生、观察、抓取、导入时间分离，发生时间可未知；瞬时、区间、不确定区间、时间精度和时间依据的基本语义，不虚构来源、顺序或完整性；
- DataOrigin、Confidence、Completeness 的含义及声明范围；旧来源未知不得默认提升为官方；
- AddOnly、Upsert、Revision、OperationHistory 的基本行为、事务和删除安全边界，保持现有 Accepted 决策；
- Portable 与数据库/Chronicle 分离，格式版本独立、稳定引用和账号重映射、未知 future major 拒绝，标准优先；
- 仅新空库初始化，已知旧版显式迁移；未知/未来数据库版本、application ID 不匹配时停止写入、保留原库，不删表重建。

[身份模型](../architecture/IDENTITY_MODEL.md) 已于 2026-10-05 接受，其档案内副本、共享身份、自然身份和导入重映射语义不再作为开放产品问题。自然身份字段编码、固定命名空间 UUID v5、Schema 3 迁移和 `Unresolved` 保留已经实现并由 fixture 覆盖；8B.1 后续实施不得把它们重新写成未冻结前置条件。

退出标准：

- 上述最小语义由 Accepted 决策明确覆盖，相关文档没有作用域冲突；
- 当前将实施的共享契约没有会改变以上四类语义的隐式选择；
- 后续字段、算法和 fixture 的冻结责任与实施时点已列明，不把未冻结格式宣称为稳定；
- 数据库和公开格式的实施门槛明确；文档收敛不能代替迁移验证或 Schema 发布验收。

已接受语义下、不写入数据库且不发布格式的 8B.1 Core 类型和纯规则可以先推进；这不等于整个 8B.0 技术核对已经完成。涉及持久化、导入引用或公开格式的代码，必须先通过相关门槛。

### 分工作包冻结责任

| 项目 | 最迟冻结时点 | 是否阻塞整个 8B.0 |
|---|---|---|
| 首批共享类型的实际存储/文件编码、自然身份迁移和未知旧数据映射 | 8B.1 首次迁移或 8B.2 首次公开使用前 | 影响上述四类语义的部分必须明确；不等待未来领域 |
| 首个 Dataset 名称、字段、Schema、标准补充载荷关联 | 8B.2 格式实现前 | 最小版本和引用原则是；整个载荷不是 |
| Revision/ChangeSet 持久化字段和撤销材料生命周期 | 8B.3 持久化前 | 基本覆盖、事务和删除语义是；全部字段不是 |
| 手工获取具体键、判重/冲突规则、周边界 | 8B.5 手工事实保存或 Portable fixture 定义前 | 稳定身份与不虚构精度原则是；领域细节不是 |
| 首批挑战期次键、观察身份、指纹、完成判定 | 8B.5 保存/导出该模式前；完成规则最迟在完成节点实现前 | 否；未实施模式不先穷举 |
| Chronicle EntryId、投影序列、Profile、分页与重建生命周期 | 8B.6 查询验证前；涉及持久化/公开格式时提前冻结相应部分 | 否；不成为领域成立条件 |
| 完整 Archive 容器 | 8C 实施前 | 否 |
| 重要成就分类元数据和完整目录 | 成就领域及相关 Chronicle 节点实施前 | 否；8B 可用虚构 fixture 验证过滤 |
| 完整 Sync/ChangeJournal、远端冲突和 Tombstone 压缩 | 对应后续功能实施前 | 否 |

任何下放项一旦马上进入持久化或公开格式，仍按上述四项判据冻结，不得因“以后再做”绕过数据安全。

## 工作包 8B.1：共享领域基础

实施状态：Completed（2026-10-05）

- 增加 GameRoleIdentityId 与自然身份值对象，保持档案内账号副本独立；
- 增加统一 DataOrigin、Confidence、Completeness 和当前领域所需的时间值对象；
- 分离来源记录引用、观察/抓取/导入时间和批次，不引入完整分布式同步模型；
- 为当前旧库设计原子迁移：未知来源安全保留，缺少身份信息不捏造、不静默合并；
- 在任何新增 Schema 前落实未知版本及 application ID 的 fail-safe，并验证失败原库不变。

当前已完成：共享身份、时间与来源 Core 类型；规范原神自然身份、固定命名空间 UUID v5、自然身份精确查找与跨档案复用；数据库 fail-safe；Schema 1/2/3→4 原子迁移；Gacha 最小来源承载；UIGF 导入和官方刷新接入；`Unresolved` 诊断。旧数据不虚构来源、抓取时间或导入时间。

本轮最小字段、编码、输入映射和迁移 fixture 见 [Phase 8B.1 实施契约](PHASE_8B1_IMPLEMENTATION.md)。

退出标准：Core 不依赖 SQLite、MAUI 或外部 DTO；当前采用的共享编码与迁移映射有说明及 fixture，旧数据库可安全原子升级。未知身份无法可靠迁移的情况必须明确报告，不默认为可迁移。

## 工作包 8B.2：领域 Portable / Export 基础

实施状态：Completed（2026-10-06；格式 Accepted、未发布，生产 Apply 留待 8B.3/8B.4）

- 为实际首批领域提供 Serializer、Validator、Importer、Exporter、Migrator 或等价的小接口，不先建设任意 Dataset 插件框架；
- 优先保持 UIGF；首个 Furina 载荷补足账号身份、来源、时间和修订等标准无法表达的语义，冻结其与标准记录的关联；
- 为领域查询提供至少一种人类可读导出路径，不经过 Chronicle；
- 对实际载荷确定 Envelope/Payload、格式名称、独立版本和 Schema；大型集合流式处理；
- 建立最小、完整、旧版、未来 major、损坏和大数据 fixture；
- 不在此冻结完整 Archive、设备游标或未来同步协议。

首个格式和本轮实施边界见 [Phase 8B.2 实施契约](PHASE_8B2_IMPLEMENTATION.md)、[Gacha Portable v1](../specifications/GACHA_PORTABLE_V1.md) 与 [ADR 0012](../decisions/0012-gacha-portable-v1.md)。生产 Apply 依赖 8B.3/8B.4 的 DataChangeSet 事务基础，本轮只提供校验、导出和只读计划，不使用临时 Upsert 绕过该门槛。

退出标准：格式有说明、Schema、样例、迁移和兼容验证；不经过 SQLite 的格式往返可用；未知 future major 拒绝且数据库不变。公开发布仍须满足 Compatibility Policy 的跨平台往返要求。

已完成：Gacha Portable v1 Codec、Schema、示例和支持矩阵；SQLite 一致快照与有界内存导出；临时文件复验后交付；只读 AddOnly Plan/Preview；CSV/XLSX 范围与来源说明；版本、损坏、身份、关联、时间、来源、大数据、取消及并发写 fixture。Windows 与 Android Release 编译通过。当前 Reader 为生成完整只读计划而在资源上限内物化 Portable Model；真正的有界导入 Apply、DataChangeSet、别名和接收上下文持久化仍属于后续工作包。

## 工作包 8B.3：变更集与修订基础

实施状态：In progress（2026-10-06；契约已由 ADR 0013 与实施文档冻结）

- 定义一次用户可理解操作的 DataChangeSet；
- 可信全量刷新覆盖、导入覆盖和手工纠正形成 Revision；
- 建立按档案计数的 OperationHistory，默认 3、可关闭、上限 1000；
- 明确存储不足确认、跨档案原子操作和不可逆删除边界；
- 保持 Revision、有限撤销与未来 ChangeJournal 的职责分离，不实现完整 Journal、设备游标或网络冲突处理。

本轮具体变化分类、版本前置条件、跨档案资格、容量淘汰、不可逆 Tombstone 和 Portable AddOnly Apply 边界见 [Phase 8B.3 实施契约](PHASE_8B3_IMPLEMENTATION.md) 与 [ADR 0013](../decisions/0013-atomic-revisions-and-bounded-undo.md)。

退出标准：业务记录和变更集同事务提交；重复输入不无限增加修订；撤销不会跨档案错误恢复或恢复已不可逆删除的数据。当前事务边界可在未来扩展，不以实现同步为条件。

## 工作包 8B.4：现有 Gacha 适配

- 将现有账号、时间和来源映射到共享基础；
- 保持 UIGF 导入导出、增量刷新和全量纠正行为，覆盖接入 Revision；
- 保持账号/档案范围的独立查询、历史查看、统计和导出；
- 验证标准载荷与 Furina 补充信息可带走并正确重导入；
- 不在此重写现有抽卡统计页面，也不以 Chronicle Contributor 完成为退出条件。

退出标准：现有抽卡回归不退化，旧记录迁移后归属和语义保持，不启用 Chronicle 仍能查询、查看历史和导出。

## 工作包 8B.5：独立领域窄纵切

按 [统一领域能力原则](../architecture/OVERVIEW.md) 验证角色/武器和挑战。允许 fixture 或文件导入作为早期输入，不要求完整官方采集和完整 UI。

### 角色与武器

- 定义完整/部分状态观察模型和稳定项目 ID，保存至少两个时间点；
- 提供仓库、SQLite 持久化、独立查询、历史比较与至少一种人类可读导出；
- 差异表达为显式派生结果；不知道真实变化时间时使用不确定区间；
- 手工获取初版至少用 fixture 验证原因、秒至周精度、稳定身份、领域合并和 Revision/OperationHistory；
- 提供 Portable fixture、重导入和引用映射验证；具体领域 Schema 在首次保存/导出前冻结。

### 挑战

- 定义游戏模式、期次、周期、账号归属、结果、来源和不可变观察；
- 首批覆盖深境螺旋；以 fixture 验证能容纳剧诗及缺少完成时间的挑战，不要求先冻结全部模式；
- 仅业务内容变化产生新观察，相同记录不重复；部分数据不得覆盖更完整数据；
- 保留真实观察/抓取时间和可取得的官方子关卡时间，不把周期或采集时间冒充完成时间；
- 提供仓库、SQLite 持久化、独立查询、历史查看、人类可读导出、Portable 与重导入；
- 首批模式的自然键、内容指纹和范围语义在保存/导出前冻结；具体完成规则在相关完成节点实施前冻结。

退出标准：两个领域都走通 Source fixture → Domain → SQLite → Query/History → Human-readable Export，以及 Portable → Re-import。即使没有 Contributor 或禁用 Chronicle，事实、历史和导出仍完整可用。

## 工作包 8B.6：Chronicle 综合消费验证

依赖 8B.4 和 8B.5 的独立领域能力，再按 ADR 0009、0010 验证综合投影：

- 实现首批贡献者，消费领域事实，不反向修改领域模型；
- 验证五星、四星角色首次/第七次、角色/武器颗粒度与每期挑战最多一个完成节点；未知顺序或不完整历史不得伪装成精确里程碑；
- 使用虚构重要成就分类 fixture 验证默认过滤，不交付完整真实成就目录；
- 以精确事件、不确定状态变化和周期/缺少发生时间的挑战验证共享语义；
- 在本工作包冻结并验证 EntryId、Profile、排序/分页和重建所需的最小子系统契约，不提升为所有领域的基础类型；
- 同秒记录与分页期间旧时间新记录等 fixture 保持；修订/撤销/删除时的游标行为在此闭合；
- 不要求完整 Chronicle UI；投影字段仍属于 Draft，直到对应契约评审完成。

退出标准：投影确定性、账号副本范围、默认产品行为与重建测试通过；删除可选索引不影响领域查询/导出，且可重建相同业务结果。Chronicle 不承担完整领域数据导出。

## 工作包 8B.7：质量门槛与交接

- 汇总各包已进行的迁移验证，不把数据安全拖到阶段末才检查；
- 更新数据库版本、实际格式与迁移说明，验证 Windows 旧库升级和 Android 新库创建；
- 验证首批领域载荷在 Windows/Android 间的语义往返；完整 Archive 往返由 8C 产品化；
- 运行抽卡回归、领域端到端、失败数据库不变和无 Chronicle 导出检查；
- 全部测试使用虚构 UID 和无效占位凭据，日志不包含秘密；
- 输出完成报告和 8C 输入清单，清楚区分已实现、Accepted target 和 Draft。

## 建议提交顺序

每个提交应可独立构建、测试和审查：

1. docs: narrow phase 8b to domain-first contracts
2. feat(core): add identity time and provenance primitives
3. feat(storage): guard and migrate shared identity and provenance
4. feat(export): add domain portable and readable export
5. feat(revisions): add changesets and bounded undo
6. feat(gacha): adapt gacha to shared foundations
7. feat(characters): add independent state history and export
8. feat(challenges): add independent observation history and export
9. feat(chronicle): consume established domain histories
10. test: verify migrations portability and phase 8b regressions

可按耦合调整提交，但不得把文档契约、数据库迁移、公开格式和大范围 UI 改造合为一个不可审查提交。

## 风险与控制

| 风险 | 控制 |
|---|---|
| 共享基础变成通用数据平台 | 只抽取首批真实领域共同语义，具体规则在领域实施时冻结 |
| Chronicle 成为领域建模中心 | 先验收独立 Query/History/Export，再消费领域事实 |
| 迁移损坏旧数据 | 新 Schema 前 fail-safe、备份、显式事务迁移及失败 fixture |
| Portable 成为 SQLite Row 或 ChronicleEntry 镜像 | 独立载荷、不经过 SQLite 的往返、禁用 Chronicle 的导出测试 |
| 为未来同步过度设计 | 保持职责边界，不实现完整 Journal 或分布式冲突协议 |
| 新领域范围失控 | 窄纵切和 fixture，不承诺完整 UI/API 或所有模式 Schema |
| Android 阻塞纯领域工作 | 先跨平台 Core/Services/Infrastructure 测试，相关包及退出时检查平台 |

## Phase 8B 完成定义

- 已接受的共享身份、时间、来源、合并和修订语义有实现，迁移安全；
- 至少一个 Furina 载荷完成说明、Schema、样例、验证和迁移，领域数据可带走；
- 抽卡兼容性及独立查询/导出保持；
- 角色/武器和挑战分别通过独立保存、查询、历史查看、可读导出与 Portable 重导入；
- 领域成立不以 Contributor 为条件，本阶段随后完成首批 Chronicle 消费验证；
- Chronicle 在至少三种时间语义及既有默认节点/颗粒度上有 fixture，未知信息不被伪造；
- 全部核心测试不依赖服务端，跨平台数据语义保持；
- 8C 可基于这些领域出口实现完整 Archive 和跨端产品闭环。
