# Phase 8B.1 实施契约

状态：Accepted implementation baseline

基线：`feat/phase-8`，起始提交 `2521d11`（2026-10-05）

## 范围

本工作包只完成共享身份收尾、当前领域需要的最小时间/来源基础，以及现有 Gacha 的兼容承载。它不建立通用记录基类、Portable Envelope、Revision、Chronicle 投影、同步日志或未实施游戏的领域框架。

已冻结并已实现的 UUID v5 命名空间、自然身份编码、Schema 3 共享身份迁移和 `Unresolved` 保留规则不重新打开。本轮在其上增量实施。

## 身份调用规则

- 账号副本判重使用 `PlayerArchiveId + GameRoleNaturalIdentity`，不得仅使用 UID。
- 已知 UID 能可靠推导区服时，规范身份必须采用推导区服；调用方提供的不同区服视为冲突。
- 只有本地规则不能推导区服时，才允许以用户明确选择的合法区服作为保险输入。
- 新建其他档案中的同角色副本时，复用库内相同自然身份已有的 `GameRoleIdentityId`；不存在时才生成已冻结的 UUID v5。
- 已解析账号修改备注不重新生成共享身份。8B.1 不定义“修改已解析 UID”是纠正身份还是更换角色，因此拒绝改变其规范自然身份，不转移或合并业务记录。
- `Unresolved` 账号可以补全。补全与同档案已有自然身份冲突时，整个更新失败，账号与既有业务记录保持不变。
- UIGF 自动匹配只使用由 UID 可靠推导出的精确自然身份；不能推导时报告冲突，不任取 UID 相同的第一条记录。

## 共享 Core 类型

本轮增加：

- `OccurrenceTime`：`Unknown / Instant / Interval / UncertainInterval`；
- `TimePrecision`：沿用 `Second / Minute / Hour / Day / Week`；
- `RecordTimestamps`：相互独立的 `ObservedAt / FetchedAt / ImportedAt`；
- `DataSourceReference`：与领域实体身份分离的 Provider、来源记录和快照引用；
- `AcquisitionBatchId`：一次实际导入或刷新批次的非空 GUID，不是同步操作 ID；
- `RecordProvenance`：来源、来源引用、独立时间和可空采集批次的组合值。

`OccurrenceTime.Unknown` 不含起止时间和精度。瞬时只有一个来源时刻；明确区间表示事实覆盖整个区间；不确定区间表示事实发生在两个边界之间但未知准确时刻。UTC 用于比较，`DateTimeOffset` 继续保留来源偏移。周精度的具体边界由使用它的领域提供，本轮不决定统一周起始日。

不增加 `SortAtUtc`、`EntryId`、Profile、投影序列、分页游标或统一领域记录基类。

## Gacha 最小持久化字段

现有 `GachaRecord.Time` 继续是来源提供的抽卡发生时间。数据库继续使用 `TimeUtcTicks + TimeOffsetMinutes`，不增加第二个可独立修改的发生时间字段。当前旧数据无法证明独立的时间精度，因此本轮不持久化 Gacha `TimePrecision`。

Schema 4 只为 `GachaRecords` 增加：

| 字段 | SQLite 编码 | 空值含义 |
|---|---|---|
| `Origin` | `INTEGER NOT NULL`，`DataOrigin` 数值 | 不使用空值；旧数据写 `Unknown = 0` |
| `FetchedAtUtcTicks` | `INTEGER NULL` | 来源抓取时间未知或不适用 |
| `FetchedAtOffsetMinutes` | `INTEGER NULL` | 与抓取 ticks 同时为空 |
| `ImportedAtUtcTicks` | `INTEGER NULL` | 本地导入时间未知或不适用 |
| `ImportedAtOffsetMinutes` | `INTEGER NULL` | 与导入 ticks 同时为空 |
| `AcquisitionBatchId` | `TEXT NULL`，GUID `D` 格式 | 旧记录或批次未知 |

本轮不持久化 `ObservedAt`、`Confidence`、`Completeness`、Provider、Collector 版本、快照引用或同步字段。现有输入没有独立观察时间；Gacha 尚未声明可证明的完整度范围，因此不得默认写入 `Complete`。

## 输入和冲突映射

| 路径 | Origin | 时间 | 批次 |
|---|---|---|---|
| UIGF 导入 | `StandardImport` | 一次用例调用的真实本地 `ImportedAt` | 每次调用一个新批次 |
| 官方抽卡接口刷新 | `OfficialApi` | 每页响应返回后的真实本地 `FetchedAt` | 每次刷新一个新批次 |
| 旧记录迁移 | `Unknown` | 抓取/导入时间均为空 | 空 |

手工 URL 和 Windows 网页缓存只提供访问官方抽卡响应的入口，不把业务事实改记为 `UserEntered` 或 `LocalObservation`。URL、AuthKey、Cookie、Token 及其他秘密不得持久化到记录元数据。

插入新记录时保存输入元数据。`PreserveExisting` 重复输入保持已有业务内容和来源元数据，不因新批次或时间形成变化；仍允许既有规则补齐缺失物品字段。可信全量刷新使用 `ReplaceExisting` 时，以新官方响应覆盖业务字段和本次来源元数据。Revision 行为仍属于 8B.3。

## 迁移

- 当前 Schema 3 增量迁移到 Schema 4；v1、v2 通过同一事务依次执行已知结构变换后到达 Schema 4。
- 所有旧 Gacha 业务字段、账号副本外键、外部记录 ID、发生时间及原偏移原样保留。
- v1、v2、v3 的迁移均只在事务成功后设置 `user_version = 4`。
- 未来版本、错误 `application_id`、损坏结构和中途异常保留原库并拒绝继续写入。
- 应覆盖新空库、v1/v2/v3、失败回滚、重新打开、重复初始化、跨档案归属和元数据往返 fixture。

## 内部开发版本限制

项目尚未公开发布，只能保证从实施未来版本保护的客户端开始，遇到更高 Schema 时拒绝写入。无法追溯保证此前内部客户端不会按其旧逻辑处理后来升级的数据库。发布流程必须使用 SQLite 一致备份；数据库可能处于 WAL 模式时不得只复制运行中的主文件。

## 退出边界

8B.1 完成时必须能够报告所有 `Unresolved` 账号及所属档案和可靠原因。它们仍可本地查看，但不得参与依赖稳定身份的自动映射。本轮不建设完整修复 UI。

