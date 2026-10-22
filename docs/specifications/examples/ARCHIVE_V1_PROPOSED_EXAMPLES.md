# Archive v1 提案示例

状态：8C.0 review fixture；不是可导入生产库的已发布样本

这些示例固定审查语义，不固定 ZIP 字节顺序。维护者接受 ADR 0021 后，8C.1 必须由正式 Writer 生成机器 fixture，并由正式 Reader 重新读取；不得手工维护与实现分叉的“黄金 ZIP”。

## M01：最小完整逻辑 Archive

内容：

- 一个名为 `空档案` 的档案；
- 零账号、零领域事实；
- `RequireUniqueArchiveNames = true`；
- 该档案 `UndoLimit = 3`。

条目：

```text
manifest.json
datasets/catalog.v1.json
settings/portable.v1.json
checksums.sha256
```

预期：

- Reader 接受；
- 语义比较必须保留空档案，不能因记录数为零而丢弃；
- 不声明游戏历史 Complete；
- 8C.2 Preview 应显示“将新建一个空档案”，但 8C.0／8C.1 不写数据库。

## C01：完整审查样本

内容：

- 两个档案，其中两个账号具有同一规范自然身份和共享身份 GUID，但业务副本独立；
- 一个空账号；
- 一个 `Unresolved` 占位账号；
- 一个 UIGF 4.2 可表达的 Gacha 当前事实；
- 一个缺 `item_id` 的 Gacha 当前事实；
- 两条 Gacha Revision，包括 A→B→A；
- 一次角色状态观察；
- 一条手工角色获取及一条来源修订；
- 两次深境螺旋观察；
- 不同原始时间偏移、Unknown／OfficialApi／StandardImport／UserEntered 来源；
- 一个完全未知发生时间；
- 每档案不同 UndoLimit。

预期：

- 同角色的两个账号共享角色身份，但所有事实仍按 `account_ref` 独立；
- 可标准化 Gacha 在 UIGF 和 supplement 中一一对应，只计一条；
- 不完整 Gacha 只存在于 Archive-native history，不伪造标准字段；
- Revision 保留，但源 Undo／ChangeSet／Receipt／Tombstone／Chronicle 均不存在于包内；
- 删除 Chronicle 表或禁用网络不影响 Writer/Reader 语义比较。

## N01：漏 Revision

从 C01 删除一条 Gacha Revision，同时保持 Manifest 计数和 checksum 自洽。

预期：字节级校验可以通过，但与来源一致快照的保真比较失败，exporter 不得将其报告为完整逻辑 Archive。

## N02：误合并档案副本

将两个档案中相同 UID 的事实改为引用同一个 `account_ref`。

预期：catalog 范围和跨文件引用校验失败；不能因共享 `role_identity_ref` 合并业务副本。

## N03：伪造标准字段

把不完整 Gacha 的显示名称复制到 `item_id`，以便放进 UIGF。

预期：fixture 被视为语义损坏；Writer 永远不能执行此转换。

## N04：历史绕过 Tombstone

目标已有对应事实的活动 Tombstone；来源只在 Revision before-image 中携带被删除内容。

预期：8C.2 Preview 报告抑制命中；8C.3 Apply 在没有绑定 marker/version 的显式重引入确认时不能写入该历史内容。

## N05：未知必需 Dataset

Manifest 增加：

```json
{
  "dataset_ref": "future-required",
  "kind": "future-domain",
  "format": "future-format",
  "format_version": "1.0",
  "required": true,
  "paths": ["datasets/future.json"],
  "record_counts": {},
  "uncompressed_length": 2
}
```

预期：Reader 可以完成容器结构诊断，但 Restore 支持判定为拒绝；不得跳过后报告完整成功。

## N06：未知可选 Dataset

与 N05 相同，但 `required = false`。

预期：Reader 校验路径、长度和哈希并返回 `UnsupportedOptionalDataset`。默认不恢复；只有维护者接受 ADR 0021 的部分恢复政策后，8C.2 才能提供明确确认路径。

## N07：容器攻击

分别构造：

- `../outside.json`；
- `datasets\\catalog.v1.json`；
- 大小写或分隔符归一化后的重复路径；
- checksum 缺项或多项；
- future Archive major；
- 超过累计展开字节；
- NDJSON 单行或 JSON 深度超限；
- 截断 ZIP。

预期：在任何目标数据库写入前结构化拒绝，诊断不包含业务原文或秘密。

## N08：跨领域不一致时点

导出 Gacha 后修改账号名称或手工获取事实，再导出其他领域。

预期：这种逐 exporter 拼包不满足 8C.1；正式 Writer 必须从同一个 SQLite 一致快照读取，测试应通过并发写入证明包内仍为单一时点。

## N09：设置冲突

来源要求唯一档案名，目标已经存在多个精确同名档案。

预期：Reader 接受文件；8C.2 Preview 必须在 Apply 前解决。不得恢复成功后让应用进入启动阻塞。

## N10：第三领域故障

恢复计划包含 Gacha、角色／武器和挑战；在挑战 mutation 或 Receipt 写入时注入故障。

预期：8C.3 的单一跨领域事务整体回滚。依次调用三个独立 Apply 再尝试补偿不算通过。
