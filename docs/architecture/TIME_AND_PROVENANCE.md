# 时间与来源

状态：Accepted

## 时间字段

- `OccurredAt`：事实实际发生时间，可为空。
- `ObservedAt`：Furina 或其他来源观察到状态的时间。
- `FetchedAt`：网络请求或本地采集完成时间。
- `ImportedAt`：外部文件进入本地数据库的时间。
- `ModifiedAt`：本地记录或修订最后改变时间。

内部使用 UTC `DateTimeOffset` 或等价明确时区的表示。格式特有规则在 Codec 边界转换。例如 UIAF 的 UTC+8 规则不能污染内部通用时间模型。

共享时间精度使用：

```text
Second
Minute
Hour
Day
Week
```

精度描述来源实际能够证明到什么程度，不能通过显示格式或排序需要提升精度。周精度必须由具体领域保存明确区间；周起始日和时区由首次使用该语义的领域冻结。

## DataOrigin

至少区分：

```text
OfficialApi
StandardImport
FurinaImport
LocalObservation
LocalCollector
UserEntered
Derived
Unknown
```

`Unknown` 用于无法可靠回溯来源的旧记录或外部数据。它不是可以被默认提升为 `OfficialApi` 的临时别名。

记录还可以包含：

- `SourceRecordId`
- `SourceSnapshotId`
- `ImportBatchId`
- `RefreshBatchId`
- `Provider`
- `CollectorVersion`
- `Confidence`
- `Completeness`
- `RawPayloadHash`

不得仅凭来源类型建立全局“谁永远覆盖谁”的优先级。不同数据域通过自己的 Merge Policy 决定。

## Confidence 与 Completeness

共享枚举为：

```text
Confidence: Confirmed | High | Medium | Low | Unknown
Completeness: Complete | Partial | Unknown
```

`Confidence` 表示该记录所表达事实的可信程度，不是来源的全局优先级。

`Completeness` 是共享基础枚举，但声明范围不全局统一。每个领域或数据类型必须定义 `Complete`、`Partial`、`Unknown` 在该类型上的具体含义。`Complete` 只相对于该类型明确声明的范围成立；不得从记录级、观察级或数据集级完整度推导更高层级的完整性，也不得仅因官方来源而默认标记为 `Complete`。

任何持久化字段或公开 Portable 字段在首次使用 `Completeness` 前，必须由所属领域规范声明：被判断的对象、覆盖范围、判定证据，以及范围未知或证据不足时如何退化。共享枚举本身不提供默认判定算法。例如“一次官方响应在该接口页内完整”不等于“该期挑战历史完整”，更不等于“账号或档案完整”。聚合层只能报告自身能够证明的完整度，不能取子项最高值或仅因所有已知子项均为 `Complete` 而自动提升。

## 原始证据保留

保留等级：

```text
Permanent
Versioned
Deduplicated
Downsampled
Ephemeral
NeverPersist
```

关键抽卡、成就、挑战和完整状态快照可长期保存或内容去重保存；实时便签等高频数据保存结构化观察，原始响应可短期保留。登录、验证码和凭据响应默认 `NeverPersist`。
