# Phase 8C.1 Archive 导出基础实施记录

状态：Completed（export foundation only）

日期：2026-10-23（Asia/Shanghai）

实施基线：`feat/phase-8 @ ebd364152303f0b12b5846d42640569d9258599c`

契约依据：[ADR 0021](../decisions/0021-logical-archive-v1-fidelity.md)、[保真矩阵](../specifications/ARCHIVE_V1_FIDELITY_MATRIX.md)和 [Archive v1 内部实施规范](../specifications/ARCHIVE_V1_PROPOSED.md)。

## 结论

8C.1 已完成内部 Archive v1 的平台无关逻辑模型、ZIP Writer、Reader／Validator、版本派发和 SQLite 一致快照导出。它证明“同一时点的受支持逻辑数据可以离线导出并由正式 Reader 重新验证”，不提供 Preview、生产 Merge、Replace 或正式 UI，因此不能称为完整备份恢复或跨端闭环。

本轮没有修改 Schema 7，没有读取真实生产数据库或凭据，也没有把 SQLite Row、生产数据库文件、Chronicle 投影或本机 Undo/Tombstone 写入公开容器。

## 实际支持范围

| 内容 | Archive v1 表达 | 备注 |
|---|---|---|
| 档案、空档案、账号、空账号 | catalog | 保留来源包内引用与完整名称 |
| 共享角色身份 | catalog role identity | 账号副本仍按 `account_ref` 独立 |
| Unresolved 账号 | catalog | 不伪造 UID、区服或共享身份 |
| UIGF 4.2 可表达的 Gacha 当前事实 | 每账号独立 UIGF + `standard_extension` | 两处按账号及记录 ID 一一校验，只计一条事实 |
| 缺少标准字段或属于 Unresolved 的 Gacha 当前事实 | Archive-native NDJSON | 不伪造 `item_id` 等标准字段 |
| Gacha Revision | Archive-native NDJSON | 保留 before/after 与事实版本，不携带源 ChangeSetId |
| 角色／武器状态观察 | 归一化领域载荷 | 保留来源、时间、Completeness 和账号归属 |
| 手工获取及其 Revision | 归一化领域载荷 | 保留当前事实和已保留历史 |
| 挑战观察 | 归一化领域载荷 | 首批仍以当前已实现挑战范围为限 |
| 可移植设置 | `RequireUniqueArchiveNames`、每档案 `UndoLimit` | 不包含设备路径、SAF URI、窗口状态、权限或秘密 |
| 源 Undo／ChangeSet／Receipt／Tombstone | 不导出 | 目标 Tombstone 处理属于 8C.2／8C.3 |
| Chronicle、元数据缓存、凭据 | 不导出 | Chronicle 可重建；缓存与秘密不是逻辑事实 |

## 实现位置

- `FurinaChronicle.Services/Archives/Portable/`：Archive DTO、限制、诊断、版本派发和导出／未来恢复端口。
- `FurinaChronicle.Infrastructure/Archives/Portable/ArchivePackageWriter.cs`：容器和逻辑 Dataset 写入。
- `FurinaChronicle.Infrastructure/Archives/Portable/ArchivePackageReader.cs`：离线、只读、有界／流式验证。
- `FurinaChronicle.Infrastructure/Archives/Portable/SqliteArchiveExportSnapshotFactory.cs`：SQLite Online Backup 一致快照和分页读取。
- `FurinaChronicle.Infrastructure/Archives/Portable/ArchiveLogicalExporter.cs`：快照、写入、正式 Reader 回读和清理生命周期。
- `FurinaChronicle.Tests/Infrastructure/Archives/Portable/`：Codec、负例、并发快照、跨领域保真和取消清理测试。

## 容器与安全边界

- Archive major 为 `1.0`；future major 和未知必需 Dataset 拒绝，未知可选 Dataset 只产生 partial 诊断。
- `checksums.sha256` 是条目哈希唯一真相，只提供损坏检测，不提供真实性或防篡改声明。
- Reader 拒绝目录穿越、反斜杠、重复路径、漏项／多项、长度或数量不符、无效父引用和资源超限。
- UIGF 和 Gacha NDJSON 不把完整记录集一次性载入内存；标准事实以顺序摘要验证一一对应。
- 默认上限包括 4096 条目、2 GiB 总展开大小、2,000,000 Gacha 当前事实、2,000,000 Gacha Revision 和每档案 100,000 条领域记录。
- 导出目标必须可 seek；临时 SQLite session 位于 app-private 目录，成功、异常和取消均清理。

## 自动化与构建证据

| 项目 | 结果 |
|---|---|
| Release 全量测试 | 547/547 passed，0 failed，0 skipped |
| Archive 定向测试 | 22/22 passed（契约、Codec、快照） |
| MAUI Windows Release | passed，0 warnings，0 errors |
| 独立 WinUI Release | passed，0 warnings，0 errors |
| Android Release | passed，0 warnings，0 errors；SDK 为 `E:\AndroidSDK` |
| Schema | 仍为 7；无迁移 |
| CI | Not run；本轮仅本地提交，尚未推送 |
| Windows／Android 设备交互 | Not run；8C.1 无正式入口 |
| 真实生产库导出／恢复 | Not run；恢复明确不在 8C.1 范围 |

自动化使用合成数据和临时数据库。测试覆盖空档案、Unresolved、不完整 Gacha、Revision、跨领域事实、标准事实大于 64 KiB 读取缓冲区、并发写入后的快照隔离、哈希损坏、路径穿越、资源上限、账号引用冲突及取消清理。

## 8C.2～8C.3 最短交接

1. 8C.2 在 app-private staging 中只读同一 Archive，生成绑定包 SHA-256、目标数据库 epoch、映射、冲突、Tombstone、空间估算和设置差异的统一 RestorePlan。
2. Preview 不得写生产库；未知可选 Dataset、目标 Tombstone 命中和名称映射必须转成明确诊断或确认项。
3. 8C.3 以一个 SQLite 事务提交父对象、Gacha、角色／武器、挑战、Revision、目标 ChangeSet／Receipt 和确认后的设置；不得顺序调用三个独立 Apply 冒充原子恢复。
4. 任一领域、Revision 或 Receipt 失败都整体回滚。完成 Merge 故障注入前不实现 Replace。
5. 正式 UI、Windows↔Android 真实 Archive 文件恢复和公开格式发布继续后置；不得继承 8B.7 的设备证据冒充本阶段证据。
