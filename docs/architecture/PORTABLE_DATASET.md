# 可移植数据集

状态：Target

## 目的

Portable Dataset 是数据库、UI 和平台无关的数据契约，可用于分类导出、全量 Archive、文件迁移和未来同步。

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

正式字段、名称和 Schema 在 Phase 8B 实现前另行冻结；此示例只定义分层方向。

## 大型数据集

抽卡、流水和长期观察等大型数据在 Archive 中优先使用：

```text
datasets/gacha.metadata.json
datasets/gacha.ndjson
```

每行一个记录，以便流式读取、验证、迁移和分批写入。不得要求把整个数组一次性加载到内存。

## 账号描述

Portable Model 需要同时表达：

- 源档案名称；
- 源 Archive GUID；
- 源 `GameAccount.Id`，仅作为包内引用；
- `GameRoleIdentityId`；
- GameBiz、Region/Server、UID；
- 可选显示名称。

导入器为 Archive 和 GameAccount 建立本地 ID 映射。业务记录通过源副本引用完成重映射，不能直接把源本地主键写入目标数据库。

## 标准格式优先

- 抽卡优先使用当前支持的 UIGF。
- 成就优先使用当前支持的 UIAF。
- Furina 数据集只承载标准无法表达的数据或其他领域。
- Furina Archive 可以包含标准数据集和 Furina 数据集，但不能把 UIGF 当作完整备份。

## Markdown

Markdown 是不可导入的人类可读输出，分为：

- 单文件摘要；
- 包含 README、分类明细和本地资源的离线报告目录；
- 可选隐私脱敏报告。

报告不得依赖在线图片 URL，默认不展开数万条明细。
