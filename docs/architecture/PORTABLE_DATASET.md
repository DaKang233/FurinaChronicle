# 可移植数据集与领域导出

状态：Target

## 目的与三类出口

Portable/Export 服务本地优先的原神个人历史档案工具，遵循领域自治、数据优先、开放导出、Chronicle 派生。用户应能带走领域数据，并脱离当前 UI 查看和分析；不以 Chronicle 或未来自动同步为前置条件。

| 出口 | 用途 | 边界 |
|---|---|---|
| 通用标准格式 | 有标准的领域优先使用标准，例如抽卡 UIGF、成就 UIAF | 按已支持版本可靠导入和导出；标准未覆盖的 Furina 语义不能假装已经保留 |
| Furina 机器可读格式 | 标准无法表达的数据、领域级分类备份、Windows/Android 文件迁移和最终全量 Archive | 公开版本化规范、Schema、示例和迁移规则；不替代已有领域标准 |
| 人类可读导出 | Markdown、文本、表格等，用于查看、归档、展示和外部分析 | 可以不可重新导入；领域直接提供，不要求先经过 Chronicle |

Portable Dataset 是数据库、UI 和平台无关的领域数据契约。事实、来源和必要历史由所属领域定义，序列化不能使用 SQLite Row 镜像；数据库版本、Dataset 版本、Archive 版本和应用版本独立演进。

## 与 Chronicle、Archive 和未来 Sync 的边界

- 领域导出不依赖 `ChronicleEntry`、`EntryId`、Profile、投影序列或游标；未进入时间线的事实仍可完整导出。
- Chronicle 报告仅是选定事实的可读叙事，不承担领域数据完整导出职责。删除索引后领域 Portable 导出和重导入必须仍可用。
- Archive 将多个领域载荷、可移植设置和附件组织为全量备份；完整容器在 Phase 8C 冻结，不阻塞领域单独导出。
- 未来同步可以消费领域载荷，但 Phase 8B 不冻结设备游标、网络冲突协议或完整 ChangeJournal。文件迁移不等于分布式同步。

## 小型独立数据集

可以使用 Envelope + Payload：

```json
{
  "format": "furina-dataset",
  "format_version": "1.0",
  "dataset": "inventory",
  "dataset_version": "1.0",
  "game": "genshin",
  "exported_at": "2026-09-29T12:00:00Z",
  "accounts": [],
  "payload": []
}
```

这是分层示例，不是已发布格式，也不要求当前实施 inventory。实际数据集名称、字段、编码、版本和 Schema 在该数据集首次持久化或公开导出前冻结；不能要求所有未来领域先有完整 Schema。

## 大型数据集

大型记录集合使用流式 NDJSON 或等价机制，便于逐条验证、迁移和受控写入，不要求一次加载全部记录。例如 Archive 可以组织为：

```text
datasets/gacha.metadata.json
datasets/gacha.ndjson
```

路径仅示意未来容器组织，不冻结 Archive 布局或抽卡载荷。抽卡实施时优先 UIGF，Furina 载荷补足身份、来源、修订等标准不能表达的语义；标准记录与补充内容的关联规则在 8B.2 冻结，不能作为两个独立事实重复计数。

## 账号与实体引用

Portable Model 需要表达导出范围与重映射所需信息：

- 源档案名称；
- 源 Archive GUID；
- 源 `GameAccount.Id`，仅作为包内引用；
- `GameRoleIdentityId`；
- GameBiz、Region/Server、UID；
- 可选显示名称。

导入器按已接受的 [身份模型](IDENTITY_MODEL.md) 建立 Archive、账号副本和共享身份映射。业务记录通过源副本引用完成重映射，不能直接把源本地主键写入目标数据库；共享角色身份不导致跨档案记录联动。

可修改事实必须有不随内容修改而改变的稳定身份。具体领域键、别名和引用编码在对应领域的持久化/Portable 门槛冻结；Chronicle 的本地 EntryId 不能代替它们。

## 标准格式优先

- 抽卡优先使用当前支持的 UIGF。
- 成就优先使用当前支持的 UIAF。
- Furina 数据集只承载标准无法表达的数据或其他领域。
- Furina Archive 可以包含标准数据集和 Furina 数据集，但不能把 UIGF 当作完整备份。
- 导出范围、哪些语义由标准保留、哪些需要 Furina 补充，应在领域格式说明中写清楚。

## 人类可读导出

领域查询应直接支持 Markdown、文本或表格输出，不要求实现全部格式。输出应说明账号/档案范围、时间精度、来源、不完整或派生标记，以及生成时间。

Markdown 是不可导入的人类可读输出，分为：

- 单文件摘要；
- 包含 README、分类明细和本地资源的离线报告目录；
- 可选隐私脱敏报告。

离线报告不得依赖在线图片；摘要默认不展开数万条明细，但应提供明确的明细导出能力。无法重新导入不影响它作为长期查看与外部分析出口的价值。

## 冻结与验收

8B.0 冻结版本独立、引用重映射、稳定身份和兼容行为等最小原则。8B.2 及领域实施前冻结实际格式，并同时提供说明、Schema、最小/完整示例、迁移与兼容测试；发布要求遵守 [兼容策略](../principles/COMPATIBILITY_POLICY.md)。

首个实际领域格式已经由 [ADR 0012](../decisions/0012-gacha-portable-v1.md) 冻结为 `furina-gacha-portable` v1：一个源档案的多个已解析原神账号副本使用单领域 ZIP，标准事实由 UIGF 4.2 承载，Furina 补充载荷保存账号引用、原始时间表示和来源。具体字段、支持范围和资源限制见 [Gacha Portable v1](../specifications/GACHA_PORTABLE_V1.md)。该格式当前为 Accepted、未发布，不预定完整 Archive 布局。

验收必须覆盖不经过 SQLite 的格式往返、不启用 Chronicle 的领域导出、重导入幂等、未知 future major 拒绝且数据库不变，以及 Windows/Android 的语义一致。不得因为缩小 8B.0 范围而提前宣称格式稳定。
