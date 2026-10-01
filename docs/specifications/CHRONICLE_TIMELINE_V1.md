# Chronicle Timeline v1 草案

状态：Draft

本文件定义首版跨数据域游戏历程的内部可移植语义。它不是已发布的交换格式，字段名和枚举值只有在 ADR 0009 获得确认并完成 Phase 8B fixture 后才冻结。

## 1. 范围

本规范覆盖：

- 游戏历程条目的共同时间、来源、身份和排序语义；
- 账号与档案范围查询；
- 数据域贡献者、分页、关联和重建；
- 与修订、导出和同步的边界。

本规范不覆盖：

- 具体 UI 布局；
- 各数据域全部业务字段；
- 档案操作历史的完整格式；
- 对外发布的 JSON Schema。

## 2. 规范性术语

文中的“必须”“不得”“应”“可以”分别表示硬性要求、禁止要求、推荐要求和可选能力。

## 3. 时间范围

```text
TemporalExtent
  Kind: Instant | Interval | UncertainInterval
  StartAt: DateTimeOffset
  EndAt: DateTimeOffset?
  Precision: Exact | Minute | Day | Period | BetweenObservations | Unknown
  TimeBasis: SourceOffset | ServerZone | UserZone | Unknown
  OriginalOffsetMinutes: int?
```

约束：

1. `Instant` 不得设置不同于 `StartAt` 的 `EndAt`。
2. `Interval` 和 `UncertainInterval` 必须有 `EndAt`，且不得早于 `StartAt`。
3. 内部比较必须以 UTC 时刻进行；来源偏移存在时必须保留。
4. 只知道日期时不得擅自填充精确时分秒，必须使用 `Day` 精度。
5. 两次观察之间发现的变化必须使用 `UncertainInterval` 和 `BetweenObservations`，除非来源提供了真实发生时间。

## 4. 来源与可信度

每个条目必须继承其事实来源：

```text
Origin: OfficialApi | StandardImport | FurinaImport |
        LocalObservation | LocalCollector | UserEntered | Derived
Confidence: Confirmed | High | Medium | Low | Unknown
Completeness: Complete | Partial | Unknown
```

`Confidence` 描述该条目所表达事实的可信程度，不是来源的全局排名。`Completeness` 描述记录是否覆盖声明范围，不表示内容真假。

派生条目必须引用其输入事实或可重建查询范围。官方事实与派生事实即使展示相同数值，也不得合并为同一来源。

## 5. 条目模型

```text
ChronicleEntry
  EntryId: string
  ProjectionVersion: int
  GameAccountId: Guid
  Domain: string
  Kind: string
  Temporal: TemporalExtent
  SortAtUtc: DateTimeOffset
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

- `EntryId` 在同一投影版本中必须幂等；
- `SourceEntityId` 指向规范化记录或派生事实，不得只保存数据库行号；
- `TitleKey` 和 `SummaryArguments` 用于本地化，稳定身份不得依赖本地化结果；
- 图片、图标二进制和长正文不得写入条目主体；
- `ProjectionVersion` 变化时允许重建新的条目身份，但迁移应尽量保持稳定。

推荐的稳定身份输入为：

```text
Domain + Kind + GameAccountId + SourceEntityType + SourceEntityId + ProjectionVersion
```

## 6. 排序与游标

默认顺序为：

```text
SortAtUtc DESC, EntryId DESC
```

`SortAtUtc`：

- `Instant`：`StartAt`；
- `Interval`：`StartAt`；
- `UncertainInterval`：`EndAt`。

游标必须至少包含 `SortAtUtc` 与 `EntryId`。请求游标后的下一页时，必须使用严格小于该复合键的记录，保证时间相同的条目不重复、不丢失。

刷新期间新增较新的记录可以出现在后续的新查询中，但不得插入已发放游标所代表的旧页中。

## 7. 查询范围

```text
ChronicleScope
  Account(GameAccountId)
  Archive(PlayerArchiveId, optional GameAccountIds)

ChronicleFilter
  Domains?
  Kinds?
  From?
  To?
  Origins?
  MinimumConfidence?
  Importance?
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

每个数据域必须实现等价于以下语义的契约：

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

## 10. 首批映射

### 抽卡

- 一条 `GachaRecord` 产生一个 `Instant` 条目；
- 使用记录时间作为 `OccurredAt`；
- 旧记录来源未知时不得默认标记为 `OfficialApi`；
- 卡池事件名称和横幅是可替换元数据，不进入稳定身份。

### 角色与武器状态

- 一次采集首先保存状态观察，不假装知道所有变化的精确发生时间；
- 首次观察可以生成“首次记录”观察条目；
- 两次观察之间的等级、命座、精炼、装备等变化生成 `Derived` 的 `UncertainInterval` 条目；
- 原始快照和规范化状态必须能支持重新计算差异。

### 挑战

- 赛季/期次使用官方周期；具体尝试在来源提供时间时使用事件或区间；
- 挑战结果必须带游戏模式、期次自然身份和账号归属；
- 同一期重复刷新采用领域合并策略，不因时间线投影重复写入事实。

## 11. 修订与操作历史

- 时间线默认投影当前有效版本；
- 覆盖相同记录后，旧版本保留在 Revision，但不同时出现在默认游戏历程；
- “某日导入了一批记录”属于档案操作历史，不属于游戏历程；
- 撤销、重做或不可逆删除必须使投影失效并重建受影响范围。

## 12. 可移植性

首版 Furina Dataset 应导出规范化领域记录的时间和来源字段。`ChronicleEntry` 可以作为可丢弃缓存随全量 Archive 携带，但接收方必须能忽略它。

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

Phase 8B 至少提供以下固定数据：

1. 同秒多条抽卡，用于稳定排序和分页；
2. 一条只有日期的事实；
3. 两次角色状态观察及一个不确定变化；
4. 一期跨多日挑战；
5. 同一角色在两个档案中的独立副本；
6. 官方、导入、观察、手工和派生来源混合；
7. 修订、撤销与不可逆删除后的重建结果。

