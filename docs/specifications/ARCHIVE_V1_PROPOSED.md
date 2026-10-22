# Furina 跨端逻辑 Archive v1（提案）

状态：Accepted internal implementation contract；尚未公开发布

格式标识：`furina-archive`

候选版本：`1.0`

## 用途

Archive v1 在 Windows 与 Android 之间迁移已支持领域的逻辑事实和历史。它不是数据库镜像，不包含凭据或源设备撤销栈，也不证明账号全部游戏历史完整。

本规范当前只供审查；维护者接受前不得宣称对外支持，不得用于生产恢复。

## 固定容器规则

- ZIP 路径使用 `/`，以 Ordinal 比较。
- 不允许绝对路径、反斜杠、空段、`.`、`..`、路径归一化后重复项或嵌套 Archive。
- JSON 使用 UTF-8、无 BOM；时间使用 RFC 3339 `DateTimeOffset`；GUID 使用小写 `D`。
- `checksums.sha256` 按路径 Ordinal 升序列出除自身外的每个条目，格式为小写十六进制 SHA-256、两个空格和路径，以 LF 结尾。
- 校验和只用于损坏检测，不证明作者、可信来源或文件新旧。

候选布局：

```text
manifest.json
datasets/catalog.v1.json
datasets/gacha/<account_ref>/gacha.uigf.json
datasets/gacha/history.v1.ndjson
datasets/domain/<archive_ref>/character-weapon-history.v1.json
datasets/domain/<archive_ref>/challenge-history.v1.json
settings/portable.v1.json
checksums.sha256
```

Manifest 声明实际存在的条目；没有对应事实时不要求空领域文件。空档案和空账号仍由 catalog 表达。

## manifest.json

```json
{
  "format": "furina-archive",
  "format_version": "1.0",
  "generated_at": "2026-10-23T12:00:00+00:00",
  "generator": {
    "app": "FurinaChronicle",
    "app_version": "0.8.9.170",
    "phase": "8C.0",
    "commit": "0000000",
    "source_platform": "windows"
  },
  "declared_scope": {
    "kind": "all_archives",
    "archive_refs": [
      "00000000-0000-0000-0000-000000000101"
    ],
    "completeness_assertion": "supported_logical_scope"
  },
  "datasets": [
    {
      "dataset_ref": "catalog",
      "kind": "archive-catalog",
      "format": "furina-archive-catalog",
      "format_version": "1.0",
      "required": true,
      "paths": ["datasets/catalog.v1.json"],
      "record_counts": {
        "archives": 1,
        "accounts": 1,
        "role_identities": 0
      },
      "uncompressed_length": 1024
    },
    {
      "dataset_ref": "gacha-history",
      "kind": "gacha-history",
      "format": "furina-archive-gacha-history",
      "format_version": "1.0",
      "required": true,
      "paths": ["datasets/gacha/history.v1.ndjson"],
      "record_counts": {
        "current_facts": 1,
        "revisions": 1
      },
      "uncompressed_length": 2048
    }
  ],
  "portable_settings_path": "settings/portable.v1.json",
  "checksums_path": "checksums.sha256"
}
```

`completeness_assertion = supported_logical_scope` 只表示通过保真矩阵的已支持对象均被表达；不表示官方历史完整。若 exporter 发现无法表达的对象，必须失败，不能降为 `none` 后静默继续。

## archive-catalog v1

Catalog 是父对象和包内引用的唯一真相：

```json
{
  "format": "furina-archive-catalog",
  "format_version": "1.0",
  "role_identities": [],
  "archives": [
    {
      "archive_ref": "00000000-0000-0000-0000-000000000101",
      "name": "空档案",
      "created_at": "2026-01-01T08:00:00+08:00",
      "updated_at": "2026-10-23T08:00:00+08:00",
      "accounts": [
        {
          "account_ref": "00000000-0000-0000-0000-000000000102",
          "role_identity_ref": null,
          "display_name": "待修复账号",
          "legacy_uid": "placeholder",
          "legacy_server": "unknown",
          "is_placeholder": true,
          "unresolved": {
            "reason": "placeholder_account"
          },
          "created_at": "2026-01-02T08:00:00+08:00",
          "updated_at": "2026-01-02T08:00:00+08:00"
        }
      ]
    }
  ]
}
```

已解析账号通过 `role_identity_ref` 指向：

```json
{
  "role_identity_ref": "00000000-0000-0000-0000-000000000103",
  "identity_id": "00000000-0000-0000-0000-000000000103",
  "natural_identity": {
    "game_biz": "hk4e_cn",
    "server": "cn_gf01",
    "uid": "100000001"
  }
}
```

显示名称和来源本地账号 GUID 不参与目标身份判定。

## Gacha

### 标准当前事实

满足 UIGF 4.2 的当前事实按账号写入独立 `gacha.uigf.json`。Manifest 的 Gacha Dataset 描述符必须列出每个标准载荷路径；Reader 按 UIGF 4.2 规则验证。

`history.v1.ndjson` 中的 `standard_extension` 与其使用 `account_ref + external_record_id` 一一关联，保存原始时间和来源；它不是第二条事实。

### 不完整当前事实

```json
{"kind":"nonstandard_current","account_ref":"00000000-0000-0000-0000-000000000102","external_record_id":"175637280","reason_codes":["missing_item_id"],"record":{"item_name":"示例物品","item_id":null,"item_type":"角色","gacha_type":"301","uigf_gacha_type":"301","rank_type":5,"count":1,"occurred_at":{"utc_ticks":638968176000000000,"offset_minutes":480},"provenance":{"origin":"unknown","fetched_at":null,"imported_at":null,"acquisition_batch_id":null}}}
```

### Revision

```json
{"kind":"revision","revision_ref":"00000000-0000-0000-0000-000000000104","account_ref":"00000000-0000-0000-0000-000000000102","external_record_id":"175637280","change_kind":"update","before_version":1,"after_version":2,"before":{"item_name":"旧名称","item_id":null},"after":{"item_name":"示例物品","item_id":null},"created_at":"2026-10-01T08:00:00+08:00"}
```

before／after 使用完整版本化 Gacha fact DTO；示例为便于阅读而省略未变化字段。真实载荷不序列化数据库私有 Snapshot JSON，也不携带源 `ChangeSetId`。

## 领域历史

角色／武器及挑战首版可复用现有 `furina-domain-history` v1 的语义模型，但容器中每个实例必须：

- 绑定一个 catalog `archive_ref`；
- 只引用 catalog 中的 `account_ref`；
- 来自同一 Archive export snapshot；
- 通过 Manifest 路径、长度、数量和 checksum 校验；
- 保持其 internal / unpublished 状态，不因被装入 Archive 而自动公开。

如复用现有 JSON 字节会产生重复父对象真相，8C.1 Codec 应在逻辑模型层归一化并验证两处内容完全一致；后续 major 可以消除冗余，但 v1 不允许冲突时任选一份。

## portable settings v1

```json
{
  "format": "furina-portable-settings",
  "format_version": "1.0",
  "require_unique_archive_names": true,
  "archives": [
    {
      "archive_ref": "00000000-0000-0000-0000-000000000101",
      "undo_limit": 3
    }
  ]
}
```

设备路径、SAF URI、窗口状态、权限、DeviceId、选择状态和秘密字段一律非法。

## Reader／Validator 状态机

```text
Inspect ZIP
  → normalize and reserve entry paths
  → bounded-read manifest
  → dispatch Archive major
  → verify declared entries, length and checksums
  → dispatch every Dataset format/version
  → validate catalog identities and references
  → stream-validate facts/history/settings
  → cross-dataset cardinality and one-to-one checks
  → return immutable logical package + diagnostics
```

任一步失败均不打开目标数据库写事务。Reader 不访问网络或 Chronicle。

## Export 状态机

```text
Validate requested scope
  → create app-private export session
  → create SQLite Online Backup snapshot
  → open snapshot read-only
  → write catalog and bounded/streaming Datasets
  → write manifest and checksums
  → reopen with Archive Reader/Validator
  → close snapshot and delete temporary SQLite files
  → hand validated file to platform saver
```

平台保存失败只报告“交付未完成”；不得把 app 私有文件存在误报为用户备份成功。

## 首版安全上限

候选默认值：

| 资源 | 上限 |
|---|---:|
| ZIP 条目 | 4096 |
| 单条路径 UTF-8 字节 | 512 |
| Manifest | 2 MiB |
| Checksums | 4 MiB |
| 单个 JSON／NDJSON 条目 | 512 MiB |
| 总展开字节 | 2 GiB |
| JSON 深度 | 32 |
| 档案数 | 1024 |
| 账号数 | 4096 |
| Gacha 当前事实 | 2,000,000 |
| Gacha Revision | 2,000,000 |
| 单领域事实＋Revision | 100,000 |

实现必须同时检查压缩包长度、逐项展开长度、累计展开长度、记录数、临时磁盘和取消。限制是防御性默认值，不是设备性能承诺；提高上限需要显式受信配置。

## 版本派发

- Archive Reader 只支持 major 1；future major 在载荷读取和数据库写入前拒绝。
- Dataset 各自派发版本；未知必需 Dataset 拒绝。
- 未知可选 Dataset 经完整路径／长度／哈希检查后报告 `UnsupportedOptionalDataset`，默认不进入恢复。
- v1 没有 v0 Migrator；未来旧版迁移是确定性的显式步骤。

## 8C.0／8C.1 支持范围

| 能力 | 8C.0 | 8C.1 目标 |
|---|---|---|
| 保真矩阵／ADR／示例 | Accepted | 与实现同步维护 |
| Services 逻辑 DTO／端口 | 未实现 | 实现 |
| Archive Writer／Reader／Validator | 未实现 | 实现 |
| 一致快照导出 | 未实现 | 实现 |
| Preview／RestorePlan | 仅接口设计 | 8C.2 |
| 生产 Merge | 不实现 | 8C.3 |
| Replace | 不实现 | 8C.4 |
| 正式 UI／跨端设备恢复 | 不实现 | 8C.5～8C.6 |

## 外部标准核对

2026-10-23 核对 UIGF 官方规范：Current 仍为 v4.2。Furina 对原神声明 UIGF 支持时仍须同时具备可靠导入和导出；Archive 不改变现有 UIGF Codec 的版本支持。

规范真源：<https://uigf.org/zh/standards/UIGF.html>
