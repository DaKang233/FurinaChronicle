# Furina Gacha Portable v1 示例

状态：Accepted 格式的说明性示例；格式尚未公开发布

这些示例使用虚构 UID、GUID 和记录。实际 ZIP 中的长度和 SHA-256 由 Writer 对 UTF-8 载荷字节计算，不能复制示例占位值。可执行往返 fixture 位于 `GachaPortablePackageCodecTests` 和 `GachaPortableDatabaseExportTests`。

## 最小示例

最小包包含一个已解析账号和零条记录：

```text
manifest.json
accounts/20000000-0000-0000-0000-000000000001/gacha.uigf.json
accounts/20000000-0000-0000-0000-000000000001/supplement.ndjson
```

其中 UIGF 的 `list` 为空，`supplement.ndjson` 是零字节文件。Manifest 的 `account_count` 为 1、`record_count` 为 0；两个载荷的长度和 SHA-256 仍必须真实填写。空记录只表示选择范围内没有记录，不表示账号从未抽卡。

## 完整语义示例

完整 fixture 使用两个账号，并覆盖以下记录：

| 账号 | 记录 | 原偏移 | 亚秒 | 来源 | 来源时间 | Count | 可空展示字段 |
|---|---|---:|---|---|---|---:|---|
| 国服 | `1000000000000000001` | `+08:00` | 有 | `official_api` | `fetched_at` | 2 | 全部存在 |
| 国服 | `1000000000000000002` | `+08:00` | 无 | `unknown` | 均为空 | 1 | 名称/类型/星级为空 |
| 国际服 | `2000000000000000001` | `-05:00` | 有 | `standard_import` | `imported_at` | 1 | 全部存在 |

标准载荷中的时间统一转成 UTC 秒；补充行保留原始 UTC ticks 和偏移。例如第一条补充行：

```json
{"account_ref":"20000000-0000-0000-0000-000000000001","external_record_id":"1000000000000000001","occurred_at_utc_ticks":638953920001234567,"occurred_at_offset_minutes":480,"origin":"official_api","fetched_at":"2026-10-06T08:00:01+08:00","imported_at":null,"acquisition_batch_id":"40000000-0000-0000-0000-000000000001"}
```

Reader 必须恢复补充行保存的实际时刻、原偏移、来源时间和批次，而不是把 UIGF 的秒精度或 UTC 显示当作原始证据。

## 跨档案副本示例

同一自然角色分别存在于档案 A 和档案 B 时导出两个包。两个包可以有相同 `role_identity_id`，但各自的 `account_ref`、源档案和记录集合独立。导入计划不得因共享角色 GUID 把两个源副本或目标档案的数据合并。

## 反例

- 同一包中两个账号声明相同自然身份；
- 一个 `role_identity_id` 指向两个自然身份；
- 标准记录没有一一对应的补充行；
- 补充时间与标准 UTC 秒不一致；
- 未解析账号或记录缺少 `item_id`；
- future major、载荷哈希错误、路径穿越或超出资源限制。

这些反例必须返回结构化错误，不允许静默省略或进入生产 Apply。
