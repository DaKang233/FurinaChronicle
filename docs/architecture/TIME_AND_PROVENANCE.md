# 时间与来源

状态：Accepted

## 时间字段

- `OccurredAt`：事实实际发生时间，可为空。
- `ObservedAt`：Furina 或其他来源观察到状态的时间。
- `FetchedAt`：网络请求或本地采集完成时间。
- `ImportedAt`：外部文件进入本地数据库的时间。
- `ModifiedAt`：本地记录或修订最后改变时间。

内部使用 UTC `DateTimeOffset` 或等价明确时区的表示。格式特有规则在 Codec 边界转换。例如 UIAF 的 UTC+8 规则不能污染内部通用时间模型。

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
