# Furina Gacha Portable v1

状态：Accepted，未发布

格式标识：`furina-gacha-portable`

当前版本：`1.0`

## 范围

一个文件包只包含一个源档案及其中选择的一个或多个已解析原神账号副本。包内记录是明确的导出选择范围，不声明账号全部抽卡历史完整。首版不包含 Revision、Undo、Chronicle、完整 Archive、凭据或同步元数据。

选择范围包含 `Unresolved` 账号，或任一记录缺少非空 `external_record_id`、`item_id`、`gacha_type`、`uigf_gacha_type` 时，导出必须失败并返回结构化诊断。不得静默跳过或用显示名称、元数据缓存和本地化结果伪造必需字段。

## 容器

文件是 ZIP，路径使用 `/`，路径比较使用 Ordinal。固定结构为：

```text
manifest.json
accounts/<account_ref>/gacha.uigf.json
accounts/<account_ref>/supplement.ndjson
```

`account_ref` 是源 `GameAccount.Id` 的小写 GUID `D` 字符串，只作为包内引用。ZIP 不允许重复路径、绝对路径、反斜杠、空段、`.`、`..`、外部引用或清单未声明的载荷。

## manifest.json

JSON 使用 UTF-8，无 BOM。时间使用 RFC 3339 `DateTimeOffset` 字符串；GUID 使用小写 `D` 格式。必需结构：

```json
{
  "format": "furina-gacha-portable",
  "format_version": "1.0",
  "game": "genshin",
  "generated_at": "2026-10-06T00:00:00+00:00",
  "source_archive": {
    "archive_ref": "00000000-0000-0000-0000-000000000001",
    "name": "Example Archive"
  },
  "scope": {
    "kind": "selected_accounts",
    "account_count": 1,
    "record_count": 1,
    "completeness_assertion": "none"
  },
  "accounts": [
    {
      "account_ref": "00000000-0000-0000-0000-000000000002",
      "role_identity_id": "00000000-0000-0000-0000-000000000003",
      "natural_identity": {
        "game_biz": "hk4e_cn",
        "server": "cn_gf01",
        "uid": "100000001"
      },
      "display_name": null,
      "uigf_path": "accounts/00000000-0000-0000-0000-000000000002/gacha.uigf.json",
      "supplement_path": "accounts/00000000-0000-0000-0000-000000000002/supplement.ndjson",
      "record_count": 1,
      "uigf_length": 512,
      "uigf_sha256": "<64 lowercase hex characters>",
      "supplement_length": 256,
      "supplement_sha256": "<64 lowercase hex characters>"
    }
  ]
}
```

显示名称不参与身份。`completeness_assertion` 在 v1 中只能为 `none`；它明确禁止从选择范围推导完整历史。Manifest、账号引用、载荷路径、长度、哈希和记录数必须相互一致。

## 标准载荷

每个 `gacha.uigf.json` 是可单独使用的 UIGF 4.2 文件，只含对应账号。Writer 使用已保存字段，不依赖联网或元数据下载：

- `uid` 来自规范自然身份；
- `id`、`item_id`、`gacha_type`、`uigf_gacha_type` 来自保存值；
- `count` 始终输出保存值；
- `name`、`item_type`、`rank_type` 仅在保存值存在时输出，不通过元数据补齐；
- `timezone` 固定为 `0`，`time` 是实际时刻转换到 UTC 后截断到秒的互操作视图；精确 ticks 与原偏移由补充载荷恢复。

标准载荷与补充载荷对相同记录的 UID、外部记录 ID 和 UTC 秒必须一致。标准文件单独导入只能保留 UIGF 自身语义，不能宣称恢复完整 Furina 来源信息。

## supplement.ndjson

每行一个 UTF-8 JSON 对象，不允许空行。关联键是 `(account_ref, external_record_id)`：

```json
{
  "account_ref": "00000000-0000-0000-0000-000000000002",
  "external_record_id": "1000000000000000001",
  "occurred_at_utc_ticks": 638953920001234567,
  "occurred_at_offset_minutes": 480,
  "origin": "official_api",
  "fetched_at": "2026-10-06T08:00:01+08:00",
  "imported_at": null,
  "acquisition_batch_id": "00000000-0000-0000-0000-000000000004"
}
```

`origin` 的 v1 值为 `unknown`、`official_api`、`standard_import`、`furina_import`、`local_observation`、`local_collector`、`user_entered`、`derived`。未知值返回结构化 `UnsupportedEnum`，不得默认为可信来源。时间保持原始偏移；批次为空表示来源端未知。

每条标准记录必须恰有一条补充记录。孤立、缺失、重复、关联冲突或 UTC 秒不一致均为无效包。补充项只恢复时间表示和来源，不是第二条抽卡事实。

## 来源端 provenance 与接收上下文

补充载荷保存来源端 `Origin / FetchedAt / ImportedAt / AcquisitionBatchId`。读取后的 ImportPlan 另行产生目标接收上下文：`FurinaImport`、新的本地导入时间、批次、包标识和映射结果。二者不能互相覆盖；本次接收也不能把来源端声明提升为已重新验证的官方证据。

Phase 8B.2 不执行生产 Apply，因此接收上下文只存在于只读 Plan/Preview。它的实际持久化在 8B.3/8B.4 首次写入前冻结。

## 版本与兼容

- v1 Reader 支持 major `1`；未知 v1 可选字段安全忽略。
- 不支持的 future major 在任何业务写入前返回 `UnsupportedVersion`。
- v1 是首版，不提供虚构 v0 Migrator。
- 删除字段、改变关联键或身份语义必须提升 major；新增可选字段通常提升 minor。
- ZIP 字节和 JSON 字段顺序不属于语义；SHA-256 只验证清单所指载荷的实际字节。

## 资源限制

默认 Reader 限制：Manifest 1 MiB、账号 128、总记录 2,000,000、单个载荷 256 MiB、总展开 512 MiB、NDJSON 单行 64 KiB、JSON 深度 32、ZIP 条目数不超过 `1 + 2 × account_count`。超过限制返回结构化错误，不尝试部分读取或写入。

限制是安全边界，不是数据完整性声明；以后可以在不改变格式语义的前提下由显式受信配置提高。

## 校验顺序

1. Inspect ZIP 与 Manifest，识别格式和版本；
2. 校验路径、条目唯一性、资源限制和 Manifest Schema；
3. 校验自然身份、共享 GUID、账号引用和包内唯一性；
4. 流式校验长度、SHA-256、UIGF 和 NDJSON；
5. 校验记录数、关联一一对应、UTC 秒和业务字段一致性；
6. 构造当前 Portable Model；
7. 只读 Plan/Preview 解析目标映射。

任一步失败均不得创建或修改数据库对象。

## Fixture

- 最小：一个已解析账号、零条记录；
- 完整：两个账号，包含不同原偏移、亚秒、Unknown/OfficialApi/StandardImport、空与非空抓取/导入时间、非默认 Count 和可空展示字段；
- 身份反例：同自然身份不同 GUID、同 GUID 不同自然身份、相同角色来自不同档案；
- 关联反例：重复标准 ID、缺失/孤立/冲突补充项、UID/时间不一致；
- 容器反例：future major、哈希/长度不符、重复或穿越路径、截断、超限；
- 支持范围反例：Unresolved 账号、缺少 UIGF 必需字段。
