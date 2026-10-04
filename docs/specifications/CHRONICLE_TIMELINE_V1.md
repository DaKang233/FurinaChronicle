# Chronicle Timeline v1 草案

状态：Draft

本文件定义首版 Chronicle 子系统的读取投影语义。ADR 0009、0010 的核心决定与产品行为继续 Accepted，但本文件不是已发布交换格式，也不是所有领域的共享持久化模型。

字段名、投影身份、Profile、序列和游标在 8B.6 Chronicle 验证前冻结；若其中某部分更早进入持久化或公开格式，则提前冻结该部分。8B.0 只冻结真正共享且影响数据解释、跨端身份、公开兼容或迁移安全的最小契约，不等待整个草案。

领域自治、数据优先、开放导出、Chronicle 派生的作用域见 [ADR 0011](../decisions/0011-domain-first-product-scope.md)。领域先独立保存、查询和导出，Chronicle 后消费。

## 1. 范围

本规范覆盖：

- 游戏历程条目的共同时间、来源、身份和排序语义；
- 账号与档案范围查询；
- 数据域贡献者、分页、关联和重建；
- 与修订、导出和同步的边界。

本规范不覆盖：

- 具体 UI 布局；展示模式和默认可见节点仅引用已接受的产品需求；
- 各数据域全部业务字段；
- 档案操作历史的完整格式；
- 对外发布的 JSON Schema。
- 领域独立查询、历史查看和完整导出能力；这些不以 Contributor 或 Chronicle 查询服务为前置条件。

## 2. 规范性术语

文中的“必须”“不得”“应”“可以”分别表示硬性要求、禁止要求、推荐要求和可选能力。

这些术语描述草案实现后的契约要求，不表示具体字段已发布或已完成冻结。共享 Identity、Time、Provenance、Completeness 以各自基础文档为准；下面的 ChronicleTime、SortAtUtc、ChronicleEntry、EntryId、ProjectionSequence、Profile 和游标属于 Chronicle，不得反向成为所有领域的保存字段。

## 3. 时间范围

```text
TemporalExtent
  Kind: Instant | Interval | UncertainInterval
  StartAt: DateTimeOffset
  EndAt: DateTimeOffset?
  Precision: Exact | Minute | Hour | Day | Week | Period | BetweenObservations | Unknown
  TimeBasis: SourceOffset | ServerZone | UserZone | Unknown
  OriginalOffsetMinutes: int?

ChronicleTime
  Occurred: TemporalExtent?
  ObservedAt: DateTimeOffset?
  FetchedAt: DateTimeOffset?
  SortAtUtc: DateTimeOffset
  SortBasis: Occurred | Observed | Fetched
```

约束：

1. `Instant` 不得设置不同于 `StartAt` 的 `EndAt`。
2. `Interval` 和 `UncertainInterval` 必须有 `EndAt`，且不得早于 `StartAt`。
3. 内部比较必须以 UTC 时刻进行；来源偏移存在时必须保留。
4. 只知道日期或周时不得擅自填充精确时分秒；必须分别使用 `Day` 或 `Week`。周精度使用包含该周边界的区间表达，不选择虚假的周内瞬时值。
5. 两次观察之间发现的变化必须使用 `UncertainInterval` 和 `BetweenObservations`，除非来源提供了真实发生时间。
6. 来源没有发生/完成时间时，`Occurred` 必须为空；不得把 `ObservedAt` 或 `FetchedAt` 写入 `Occurred`。
7. `SortBasis` 必须说明排序锚点来自发生、观察还是抓取时间，UI 据此显示“发生于”“观察于”或“采集于”。

## 4. 来源与可信度

每个条目必须继承其事实来源：

```text
Origin: OfficialApi | StandardImport | FurinaImport |
        LocalObservation | LocalCollector | UserEntered | Derived | Unknown
Confidence: Confirmed | High | Medium | Low | Unknown
Completeness: Complete | Partial | Unknown
```

`Confidence` 描述该条目所表达事实的可信程度，不是来源的全局排名。`Completeness` 描述记录是否覆盖声明范围，不表示内容真假。

派生条目必须引用其输入事实或可重建查询范围。官方事实与派生事实即使展示相同数值，也不得合并为同一来源。

## 5. 条目模型

```text
ChronicleEntry
  EntryId: string
  PortableEntityKey: string?
  ProjectionVersion: int
  ProjectionSequence: long
  GameAccountId: Guid
  Domain: string
  Kind: string
  Timelines: set<Main | Character | Weapon | Challenge | string>
  Milestone: string?
  Time: ChronicleTime
  Origin: DataOrigin
  Confidence: Confidence
  Completeness: Completeness
  SourceEntityType: string
  SourceEntityId: string
  CorrelationId: string?
  TitleKey: string
  SummaryArguments: map<string, scalar>
  Importance: Critical | Major | Normal | Minor
```

要求：

- `EntryId` 在同一本地账号副本和同一投影版本中必须幂等；
- `PortableEntityKey` 存在时必须由共享角色身份和领域自然键生成，不得包含本地档案或账号副本 GUID；
- `Timelines` 表示候选条目可进入哪些时间线；默认可见性和颗粒度由 Profile 决定，不由是否保存条目决定；
- `SourceEntityId` 指向规范化记录或派生事实，不得只保存数据库行号；
- `TitleKey` 和 `SummaryArguments` 用于本地化，稳定身份不得依赖本地化结果；
- 图片、图标二进制和长正文不得写入条目主体；
- `ProjectionVersion` 变化时允许重建新的条目身份，但迁移应尽量保持稳定。

推荐的本地投影身份输入为：

```text
Domain + Kind + GameAccountId + SourceEntityType + SourceEntityId + ProjectionVersion
```

该公式不定义跨档案/跨设备身份。导出、导入及未来同步必须使用规范化领域记录的自然键或可移植实体键，不得把 EntryId 当作可移植主键。PortableEntityKey 在投影中可空不表示领域事实可以缺少跨端识别所需的身份；具体引用由所属领域 Portable 契约冻结，不能依赖 Chronicle 缓存。

## 6. 排序与游标

本节是 Chronicle 子系统的分页候选方案，不是领域持久化或 8B.0 的通用前置条件。序列分配、重建生命周期以及修订/撤销/删除/Profile 变化后的旧游标行为仍须在 8B.6 fixture 评审中闭合；当前描述只覆盖新投影插入，不能据此宣称任意修改下均有稳定快照。

默认顺序为：

```text
ProjectionSequence <= AsOfSequence,
SortAtUtc DESC, EntryId DESC
```

`SortAtUtc`：

- 已知 `Instant`：`StartAt`；
- 已知 `Interval`：`StartAt`；
- 已知 `UncertainInterval`：`EndAt`；
- 未知发生时间：依次使用 `ObservedAt`、`FetchedAt`，并设置相应 `SortBasis`。

首次查询必须取得稳定的 `AsOfSequence`。游标必须至少包含 `AsOfSequence`、`SortAtUtc` 与 `EntryId`。请求下一页时，查询必须继续限制 `ProjectionSequence <= AsOfSequence`，并使用严格小于排序复合键的记录，保证同一次分页遍历期间刷新产生的记录不会插入后续页面。

新查询可以取得更大的 `AsOfSequence` 并看到刷新结果。若实现不维护投影序列，则不得声称支持刷新期间稳定分页。

## 7. 查询范围

```text
ChronicleScope
  Account(GameAccountId)
  Archive(PlayerArchiveId, optional GameAccountIds)

ChronicleFilter
  Timeline: Main | Character | Weapon | Challenge | string
  Granularity: Default | Detailed
  Domains?
  Kinds?
  From?
  To?
  Origins?
  MinimumConfidence?
  Importance?
  Ranks?
  ItemIds?
```

档案查询必须在返回条目中保留 `GameAccountId`。同一共享角色身份在不同档案中的账号副本仍是不同数据范围，不得跨档案隐式去重。

## 8. 关联

`CorrelationId` 可以将以下条目关联：

- 同一次十连产生的多条抽卡；
- 同一期挑战的多个房间或结果；
- 同一对状态快照产生的多个角色变化；
- 同一刷新批次发现的相关事实。

关联不得改变每条事实的来源、时间精度和可独立追踪性。查询端可以折叠展示，但必须能展开。

## 9. 贡献者

选择参与 Chronicle 的数据域应在其独立保存、查询和导出能力成立后，实现等价于以下语义的契约；未参与的领域不因此失去完成资格：

```text
IChronicleContributor<TRecord>
  CanProject(record)
  Project(record, projectionContext)
```

贡献者必须：

- 是确定性的；
- 不执行网络请求；
- 不修改规范化记录；
- 明确投影版本；
- 为缺失时间、未知来源和部分数据提供可测试的退化行为。

贡献者只负责从规范化事实产生确定的候选事件。主时间线或领域时间线是否显示候选事件，由独立、版本化的 Profile 决定。Profile 变更不得反向修改领域事实。

不产生条目不能影响领域记录的保存、历史查看或完整导出；缺少时间时的投影退化由 Chronicle 冻结，不允许用当前时间伪造领域事实。

## 10. 首批映射

下列映射保持已接受的最终产品行为，不要求所有对应领域在 8B.0 完整建模。手工获取键与周编码在领域保存/导出前冻结；挑战期次/观察/指纹在首批模式保存前冻结，完成判定在对应完成节点实现前冻结；真实重要成就分类在成就领域实施前冻结。

### 抽卡及获取里程碑

- 每条 `GachaRecord` 可以产生抽卡候选事件，使用记录时间作为 `Occurred`；
- 每次五星物品获取进入主时间线，并可派生距同账号、同保底池上一个五星的抽数；缺失中间记录时必须显示未知，不得给出虚假数值；
- 四星角色第一次获取和第七次获取（达到满命）进入主时间线；数据不完整时必须标记为“本地记录中的首次”；
- 角色时间线默认显示角色首次和第七次获取，详细模式可显示每次角色抽取；武器时间线默认显示各星级武器首次获取，详细模式增加每次四星/五星武器抽取；
- 抽卡派生的角色/武器获取必须注明只覆盖抽卡记录，不代表活动、商店或其他途径；
- 旧记录来源未知时不得默认标记为 `OfficialApi`；
- 卡池事件名称和横幅是可替换元数据，不进入稳定身份。

### 手工获取事实

- 手工角色/武器获取是 `UserEntered` 的规范化领域事实，不是只存在于 UI 的注释；
- 允许秒、分钟、小时、日、周精度；周精度按区间保存；
- 角色原因首版包括砺行修远活动、周年庆活动自选常驻五星角色、周年庆活动自选限定五星角色和其他；自选活动显示可空的活动名称字段，“其他”必须有说明；
- 武器原因首版使用自由文本，不擅自复用角色专属枚举；
- 手工事实经领域去重与冲突处理后参与获取序号和首次/满命里程碑；修改和删除必须进入 Revision/OperationHistory。

### 角色与武器状态

- 一次采集首先保存状态观察，不假装知道所有变化的精确发生时间；
- 首次观察可以生成“首次记录”观察条目；
- 两次观察之间的等级、命座、精炼、装备等变化生成 `Derived` 的 `UncertainInterval` 条目；
- 原始快照和规范化状态必须能支持重新计算差异。

### 挑战

- 挑战结果必须带游戏模式、期次自然身份和账号归属；
- 每次官方采集都先计算排除请求时间等传输字段后的规范化内容指纹；相同内容不形成新观察事件，有变化的内容形成不可变观察事件并保存 `ObservedAt`/`FetchedAt`；
- 同一账号、模式、期次首次达到完成状态或首次观察到已完成时形成一个完成里程碑，并进入主时间线；后续分数等变化仍进入挑战时间线，但不重复主时间线完成节点；
- 深境螺旋使用官方返回的最后完成 Chamber 时间，幻想真境剧诗使用最后完成剧幕时间；
- 幽境危战使用采集时间。其他无法取得完成时间的挑战保持 `Occurred = null`，以观察/采集时间排序并明确标注；
- 赛季/期次周期用于上下文和过滤，不得冒充实际完成时间。

### 成就

- 只有由版本化元数据标记为“重要”的成就默认进入主时间线；
- 重要成就通常用于表达魔神任务等关键进度，但不得只靠本地化名称或关键词在运行时猜测；
- 其他成就仍保存为领域事实，可供以后成就时间线使用。

## 11. 修订与操作历史

- 时间线默认投影当前有效版本；
- 覆盖相同记录后，旧版本保留在 Revision，但不同时出现在默认游戏历程；
- “某日导入了一批记录”属于档案操作历史，不属于游戏历程；
- 撤销、重做或不可逆删除必须使投影失效并重建受影响范围。

## 12. 可移植性

首版 Furina Dataset 应导出规范化领域记录的时间和来源字段。`ChronicleEntry` 可以作为可丢弃缓存随全量 Archive 携带，但接收方必须能忽略它。

完整领域事实集合由领域导出器负责，不能从当前 Profile 可见条目反推。标准优先、Furina 机器可读载荷与人类可读出口见 [Portable/Export 边界](../architecture/PORTABLE_DATASET.md)。不启用 Chronicle 时这些出口仍应工作；完整 Archive 和未来自动同步不在本草案冻结。

Markdown 可以输出按时间排序的可读历程，并明确以下内容：

- 时间精度和不确定范围；
- 来源与派生标记；
- 账号和档案范围；
- 报告生成时间。

## 13. 兼容与演进

- 时间线投影版本独立于应用版本和数据集版本；
- 新贡献者不得改变已有领域条目的稳定身份，除非提升投影版本并提供重建说明；
- 未知数据域可以忽略展示，但规范化数据不得因此删除；
- 对外交换格式冻结后，未知 future major 必须拒绝，未知可选字段按格式兼容策略处理。

## 14. 验收 fixture

以下固定数据由 8B.6 消费已独立成立的领域来验证，不是 8B.0 的统一 blocker。真实分类目录和未实施领域可用明确的虚构 fixture，不视为已经产品化：

1. 同秒多条抽卡，用于稳定排序和分页；
2. 一条只有日期的事实；
3. 两次角色状态观察及一个不确定变化；
4. 一期跨多日挑战；
5. 同一角色在两个档案中的独立副本；
6. 官方、导入、观察、手工和派生来源混合；
7. 修订、撤销与不可逆删除后的重建结果。
8. 主时间线只显示五星、四星角色首次/第七次、挑战完成和重要成就的默认过滤结果；
9. 角色与武器默认/详细颗粒度及抽卡来源范围提示；
10. 秒、日、周精度的手工获取事实及里程碑重算；
11. 同一期挑战的相同响应、变化响应、首次完成和完成后变化；
12. 缺少完成时间的幽境危战或挑战记录使用采集时间排序，且不伪造 `Occurred`；
13. 分页期间插入一条旧时间的新投影，旧游标仍返回稳定快照。
