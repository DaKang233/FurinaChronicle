# ADR 0018：Chronicle generation、游标与证据退化

状态：Accepted

确认日期：2026-10-19

## 背景

8B.6 首次实现跨 Gacha、手工获取、角色／武器观察和挑战观察的 Chronicle 读取投影。稳定身份、完全无时间事实、获取序号、分页快照以及修订／删除后的旧游标行为会直接影响用户看到的数据解释，必须在首次持久化 Chronicle 缓存前冻结。

## 选项

1. 每页实时读取当前事实，仅用 `SortAt + EntryId` 翻页。
2. 建立完整领域 MVCC 或通用 ChangeJournal。
3. 建立有界、不可变、可重建的 Chronicle generation，并用最小事务内 scope epoch 判定新鲜度和破坏性失效。

## 决定

采用选项 3。

- Contributor 是无网络、无领域写入的确定性读取投影；EntryId 由本地账号副本、领域、条目种类、稳定来源引用和投影规则版本编码，不依赖名称、图标、当前时间、行号或可变内容。
- 基础事实来源与派生注释来源分开保存。状态差异、序号、里程碑、间隔和完成判断为 `Derived` 注释，不覆盖源事实的 `OfficialApi`、`StandardImport`、`UserEntered` 等来源。
- 完全没有 Occurred／Observed／Fetched 时间的条目进入 `Undated` 分区，`SortAtUtc` 为空、`SortBasis = Unknown`；不得用 ImportedAt、构建时间或 Unix epoch 冒充事实时间。
- 有时间条目按 `SortAtUtc DESC, EntryId DESC` 排列，Undated 随后按 EntryId 排列。时间窗口使用 UTC 半开区间；区间按范围重叠判断，默认不把 Undated 塞入时间窗口，调用者须显式 `IncludeUndated`。
- generation 在一个 SQLite 一致读取事务中由当前有效领域事实构建，完成后原子发布；失败、取消、空间不足或构建期间事实 epoch 改变时不得发布半份结果。
- 游标绑定 generation、scope、Profile、颗粒度、过滤指纹、投影／规则／元数据版本和最后排序键。scope 或过滤不匹配受控拒绝。
- 新增事实只推进 source epoch：新查询重建，新事实不会漂入已打开的旧 generation；有效旧游标可继续读取其不可变快照。
- 纠正、普通删除、Undo、不可逆删除和父范围 purge 推进 destructive epoch，使旧游标返回 `CursorInvalidated`。缓存清除、generation 回收、损坏或版本变化返回 `CursorExpired`／版本错误，不悄悄切到新第一页。
- 每个 scope 最多保留当前和前一代 generation；缓存有明确生命周期并可删除重建，不是同步日志或领域事实。
- 不可逆删除和 purge 在同一业务事务中清除本机可管理的相关 Chronicle 具体缓存，并推进失效状态。普通投影构建失败不得回滚已经提交的领域事实。
- 获取顺序结论显式区分 `Known / Unknown / Ambiguous` 和适用范围。没有覆盖与可靠顺序证据时只能称“本地记录”或未知；同秒字符串序、连续外部 ID、保底上限和两端五星均不能证明真实完整顺序。
- 手工获取与 Gacha 不因相近时间自动合并。状态命座只能证明观察时状态，不能倒推若干精确获取事件。
- 角色／武器状态变化仅在 Profile 显式 `IncludeStateChanges` 时显示，默认关闭；开关不改变领域事实。
- 首版不提供 Redo。

## 原因

该方案能防止旧时间新记录污染分页，同时避免为本地派生读取建立完整 EventStore、MVCC 或同步协议。事务内 epoch 只表达缓存新鲜度，不进入 Portable，也不取代 ChangeSet。

## 后果

- 主数据库需要保存最小 scope epoch 与可重建 generation 缓存；它们必须通过显式 Schema 迁移创建。
- 所有生产业务写入、Undo 和 purge 必须在自己的事务内推进相应 epoch；NoOp 和 OperationId 重试不得推进。
- 跨端导入后比较领域稳定引用和规范业务结果，不比较本地 EntryId 或 generation token。
- Profile、规则或元数据版本变化需要重建，但不迁移领域事实。

## 迁移影响

Schema 6 以前没有 Chronicle 缓存。迁移只创建空 scope 状态和缓存表，不从旧事实伪造 epoch、时间、完整度或投影。缓存不进入领域 Portable。

## 测试要求

- 同输入和规则重复构建得到相同 EntryId 与规范业务结果；语言、图标和显示名称不影响身份。
- 同秒、Timed＋Undated、小页尺寸遍历无重无漏。
- 新增旧时间事实不污染旧 generation；纠正、删除、Undo 和 purge 使旧游标显式失效。
- scope、Profile、过滤和版本不匹配游标受控拒绝。
- 缓存失败或取消不发布半代，不改变领域事实；缓存删除后可确定重建。
- 不启用 Chronicle 时领域查询、历史、Markdown 和 Portable 保持可用。

## 确认

维护者于 2026-10-19 要求按 Phase 8B.6 规划实施，接受本 ADR 所列推荐首版语义。
