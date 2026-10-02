# Phase 8B 实施计划

状态：Accepted target

维护者已经确认 ADR 0009、Q06 和本计划的实施顺序。Phase 8B 从 8B.0 契约冻结开始；数据库迁移、公开格式实现和新数据域业务代码须等待相关字段、自然键与迁移 fixture 冻结。

## 阶段目标

Phase 8B 把 FurinaChronicle 从“抽卡功能应用”推进为“可扩展的本地游戏历史档案平台”。本阶段优先冻结跨领域基础契约，再以抽卡、角色/武器状态和挑战记录验证契约。

本阶段不追求完整的新页面、所有官方接口或云同步。

## 工作包 8B.0：决策冻结

- 登记已接受的 ADR 0009、ADR 0010 和 Chronicle 产品需求，并冻结 Chronicle Timeline v1 的持久化字段；
- 核对身份、时间、来源、修订、导入合并和服务端扩展文档之间无冲突；
- 为首个公开文档化的数据契约确定命名、版本号和兼容策略；该契约服务于 Furina 备份、迁移和未来同步，不与 UIGF 等领域标准竞争；
- 记录所有仍需人工选择且会影响持久化格式的问题。

退出标准：影响数据库和公开格式的语义均为 `Accepted`，没有由 Agent 隐式决定的开放问题。

## 工作包 8B.1：共享领域基础

- 增加 `GameRoleIdentityId` 与自然身份值对象，保持档案内账号副本独立；
- 增加统一的 `DataOrigin`、`Confidence`、`Completeness`；
- 增加瞬时、区间、不确定区间、时间精度和时间依据值对象；
- 增加来源记录引用、抓取/观察/导入时间和批次标识；
- 设计数据库迁移：旧抽卡记录不得被虚假标记为官方来源。

退出标准：Core 类型不依赖 SQLite、MAUI 或外部 DTO；旧数据库可原子升级且回归测试通过。

## 工作包 8B.2：Portable Dataset 核心

- 实现 `IPortableDataset`、Serializer、Validator、Importer、Exporter、Migrator；
- 固定 Envelope + Payload 结构；
- 大型集合使用流式 NDJSON 或等价机制；
- 区分 Archive 版本、Dataset 版本和应用版本；
- 建立最小、完整、旧版、未来 major、损坏和大数据 fixture；
- 先为账号身份与抽卡数据提供首个 Furina Dataset；它具有公开机器可读规范，但定位是 Furina 备份/迁移载荷，不替代抽卡交换标准 UIGF。

退出标准：格式有说明、Schema、样例和迁移测试；未知 future major 拒绝且不修改数据库。

## 工作包 8B.3：变更集与修订基础

- 定义一次用户可理解操作的 `DataChangeSet`；
- 可信全量刷新覆盖、导入覆盖和手工纠正形成 Revision；
- 建立按档案计数的 `OperationHistory`，默认 3、可关闭、上限 1000；
- 明确存储不足确认、跨档案原子操作和不可逆删除边界；
- 为未来 ChangeJournal 预留事务钩子，不在本阶段实现服务端同步。

退出标准：业务记录和变更集同事务提交；撤销不会跨档案错误恢复或恢复已不可逆删除的数据。

## 工作包 8B.4：抽卡迁移与 Chronicle 纵切

- 将现有抽卡来源映射到共享来源模型；
- 保持 UIGF 导入导出、增量刷新和全量纠正行为；
- 实现首个 `IChronicleContributor<GachaRecord>`；
- 实现主时间线的五星抽取、四星角色首次获取和第七次获取节点，以及角色/武器时间线的首批可见性 Profile；
- 用同秒多条记录验证稳定排序、游标分页和账号/档案范围；
- 不在此时重写现有抽卡统计页面。

退出标准：现有抽卡测试不退化；删除可选 Chronicle 索引后能够重建相同结果。

## 工作包 8B.5：提前验证角色/武器仓库与挑战记录

基础契约完成后，提前建立两个窄纵切，不等待原路线图 Phase 10/11 才首次建模。

### 角色与武器状态仓库

- 定义完整/部分状态观察模型和自然项目 ID；
- 保存至少两个时间点的规范化状态；
- 以派生差异表达等级、命座、精炼或装备变化；
- 为手工角色/武器获取事实预留低精度时间、原因、修订与来源语义，初版至少用 fixture 验证；
- 不知道真实变化时间时使用不确定区间；
- 提供仓库接口、SQLite 实现、Portable fixture 和 Chronicle 贡献者；
- 初版允许用 fixture 或文件导入验证，不要求同时完成官方在线采集和完整 UI。

### 挑战记录

- 定义游戏模式、期次、周期、账号归属、结果和来源；
- 首批覆盖深境螺旋，并保证模型能扩展到幻想真境剧诗和幽境危战；
- 每次官方采集仅在规范化内容变化时形成新的观察事件；相同记录不重复，部分数据不得覆盖更完整数据；
- 深境螺旋和幻想真境剧诗优先使用最后完成 Chamber/剧幕的官方时间；幽境危战和没有官方完成时间的记录使用采集时间排序并明确标注；
- 提供仓库接口、SQLite 实现、Portable fixture 和 Chronicle 贡献者；
- 初版允许用 fixture 验证，真实 Provider 接入统一采集框架后再产品化。

退出标准：两个领域都走通 `Source fixture → Domain → SQLite → Portable → Re-import → Chronicle`，证明基础架构不只适用于抽卡。

## 工作包 8B.6：质量门槛与交接

- 更新架构图、数据库版本、公开格式说明和迁移说明；
- 全部测试使用虚构 UID 和无效占位凭据；
- 验证 Windows 数据库升级和 Android 新库创建；
- 检查日志不包含 Cookie、SToken、authkey 或原始秘密；
- 输出 Phase 8B 完成报告和 Phase 8C 输入清单。

## 建议提交顺序

每个提交都应可构建、可测试并保持旧数据安全：

1. `docs: accept phase 8b contracts`
2. `feat(core): add identity time and provenance primitives`
3. `feat(storage): migrate shared identity and provenance`
4. `feat(portable): add versioned dataset pipeline`
5. `feat(revisions): add changesets and bounded undo foundation`
6. `feat(chronicle): project gacha records into timeline entries`
7. `feat(characters): add state observation repository vertical slice`
8. `feat(challenges): add challenge record vertical slice`
9. `test: add phase 8b cross-platform and migration fixtures`

实际提交可以因代码耦合调整，但不得把数据库迁移、公开格式和大范围 UI 改造塞入同一不可审查提交。

## 风险与控制

| 风险 | 控制 |
|---|---|
| 一次迁移太多旧抽卡数据 | 先备份、事务迁移、旧来源使用安全默认值、提供迁移 fixture |
| Portable Model 变成 SQLite Row 镜像 | 契约放在独立模型，测试使用不经过 SQLite 的往返 |
| 提前新领域导致范围失控 | 只做窄纵切和 fixture，不承诺完整 UI/API |
| Chronicle 被当作事实表 | ADR 0009、重建测试和导出测试共同约束 |
| 撤销与未来同步耦合 | OperationHistory 与 ChangeJournal 分离，只共享明确变更标识 |
| Android 阻塞桌面基础开发 | Core/Services/Infrastructure 先用跨平台测试；各工作包结束时再做 Android 编译门槛 |

## Phase 8B 完成定义

- 已接受的共享身份、时间、来源和修订语义有代码实现；
- 至少一个 Furina Dataset 完成 Schema、样例、验证和迁移；
- 抽卡兼容性保持；
- 角色/武器状态和挑战记录两个窄纵切通过端到端测试；
- Chronicle 查询契约在至少三种不同时间语义上验证；
- 主时间线默认筛选、角色/武器颗粒度、手工周精度时间和挑战变化观察均有 fixture 测试；
- 不依赖服务端即可完成全部核心测试；
- Phase 8C 可以直接基于这些接口实现全量 Archive 和 Windows/Android 往返。
