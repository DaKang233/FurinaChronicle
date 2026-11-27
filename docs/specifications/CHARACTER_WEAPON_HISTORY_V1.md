# 角色／武器历史与 Portable v1

状态：Accepted implementation；内部格式未发布

本规范的持久化和 Portable 语义依赖 Accepted ADR 0016。v1 字段由 [JSON Schema](schemas/character-weapon-history-v1.schema.json)、示例和自动化共同冻结；完成 Windows↔Android 真实文件往返前仍不对外宣称公开发布。

## 数据集

- `character-weapon-observations`：不可变状态观察、范围和条目；
- `manual-acquisitions`：稳定手工事实及其来源修订链。

## 完整度

所有新官方 API 成功采集记录按 [ADR 0032](../decisions/0032-official-api-record-completeness.md) 声明 Complete，成功须覆盖响应全部字段。下列集合与条目范围继续适用：已装备武器响应不等于完整武器仓库。米游社角色详情采集按下述分批校验规则实现；其他入口的适配状态见 [实施差距](../development/OFFICIAL_API_FIELD_COVERAGE.md)。

观察头声明项目种类和集合覆盖：`Complete` 表示该来源在该次观察声明的角色或武器集合已全部枚举；`Partial` 表示只覆盖已列项目；`Unknown` 表示无法证明集合边界。

每个条目另行声明字段集合。条目 `Complete` 只表示所声明的等级／命座或等级／精炼／数量字段均由来源提供。未知字段使用空值，不编码为零。任何低层完整度都不提升为账号仓库、账号历史或档案完整。

## 可读导出

首版提供 Markdown，列出档案、账号、观察引用、观察／抓取时间、来源、集合范围、条目字段、明确的“未提供”空值和 Derived 比较。比较只说明两个观察点之间的变化及确定性，报告不得把后一次观察时间显示成升级或获得时间。

## Portable v1

Portable 保存来源账号自然身份和共享身份引用、全部不可变观察、手工当前事实及修订链。目标数据库重新生成本地 `GameAccountId`，保留稳定观察／获取引用；来源 provenance 与目标接收 Receipt 分开。

不包含本机 Undo、UndoLimit、OperationId、ChangeSetId、LocalOnlyTombstone、图标缓存、显示名称或 SQLite Row。修订使用自身稳定 `RevisionId` 串联事实版本；导入时重新归属目标端本地 ChangeSet。future major、未知枚举、悬空引用、同 ID 不同内容和资源超限在写数据库前结构化拒绝。

- 格式名：`furina-domain-history`；`dataset_kind = 1`；版本 `1.0`。
- 单文件 JSON，最大 10 MiB、最多 100,000 个当前事实／修订；读取使用 64 KiB 分块并在反序列化前限制大小。
- `package_fingerprint` 是规范 JSON 内容的 SHA-256，用于损坏与预览后替换检测，不声明作者真实性。
- Plan 只读；Apply 重验指纹、账号映射、目标计数、Tombstone 和修订身份，父实体、事实、修订、Receipt、ChangeSet 同事务。
- 同包再次 Apply 为 NoOp；同稳定引用相同内容跳过，不同内容 Conflict。没有 v0 或旧版 Migrator；future major 拒绝。
- 额外未知属性为 minor 扩展保留；当前实现遇到未知枚举或无法构造的语义值时拒绝。

最小、完整、损坏和 future-major 文件位于 `examples/`。空账号列表的 minimum 示例仅证明 Envelope/Codec 最小结构，不是可 Apply 的业务数据集；Apply 至少需要一个受支持事实。

## 米游社角色详情和 Portable 2.0

角色观察的条目可携带 character_detail；该观察可携带一份 character_metadata。详情含官方身份显示信息、好感及突破、当前武器、命座及解锁状态、天赋及倍率、角色属性的四类分组、已装备圣遗物及套装、皮肤和推荐属性。每个词条的 hits 来自官方 times；空值与明确零值分别保存。官方数值格式按原始字符串保留。属性字典、推荐选项及 Wiki 映射保存一次。UI 可使用已缓存元数据补齐名称或图像，采集值与静态展示信息各自有来源。

米游社路径先读取角色名单，再按最多 32 个 ID 分批取得详情。所有批次解析、校验通过，且每批返回 ID 集合与请求一致后，才提交不可变观察。已声明字段的类型错误、未知字段、重复属性、遗漏或重复角色返回受控失败。角色装备观测与已有按武器类型汇总同一次提交。后者的覆盖范围仍是已装备武器，其他武器数量不可由此得到。

存在详情或官方属性目录的分类包写入 2.0，完整 Archive 的角色／武器子数据集写入必需的 1.5。旧包可读，空的新增字段不改变既有私有载荷或旧包指纹。无新字段的旧记录保持原导出版本；旧客户端拒绝包含详情的新包。出现命座或天赋强化信息时，分类包使用 2.1，完整 Archive 使用必需的角色／武器数据集 1.6。旧版本读取和指纹规则保留。强化前后倍率索引按官方序列保存，未提供与空数组有独立含义。SQLite 仍使用现有不可变事实表。数据类型见 [JSON Schema](schemas/character-weapon-history-v1.schema.json)。

天赋强化字段示例：

~~~json
{
  "enhancement": {"can_enhance": true, "is_enhanced": true, "description": "<b>强化后的效果</b>"},
  "before_enhancement_indexes": [],
  "after_enhancement_indexes": [0]
}
~~~

角色详情未返回角色突破时，按同一 UID 和区服调用官方养成计算器同步列表。按角色 ID 补齐突破并保存独立的 ascension_evidence，优先使用角色详情直接提供的值。计算器分页缺失、无法映射或未获授权时，突破保持未知。武器突破只用官方明确字段。Snap.Metadata 的机制、图标与按天赋等级查得的倍率仅补充展示，不回写快照或替代玩家实际等级。
