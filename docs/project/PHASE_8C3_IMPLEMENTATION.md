# Phase 8C.3 跨领域原子 Merge 实施记录

状态：Implementation complete；product/device acceptance not started

日期：2026-10-23（Asia/Shanghai）

版本：`0.8.12 / 8C.3`

数据库：Schema 8

## 结论

8C.3 已实现内部 `IArchiveAtomicRestorer`。通过 8C.2 验证并 staging 的 Archive
可以按同一个 RestorePlan，在一个 SQLite 写事务内 Merge 到目标数据库。实现没有顺序
调用 Gacha、角色／武器和挑战的三个独立 Apply，也没有复制来源数据库、SQLite Row、
Undo 栈、Receipt、LocalOnlyTombstone 或 Chronicle generation。

本记录只关闭 8C.3 的工程范围。应用尚无正式“备份与恢复”产品入口，未对真实生产库、
Windows／Android 系统文件选择器或真机执行 Archive Apply；Replace 仍属于 8C.4。

## 实施契约

- [ADR 0022](../decisions/0022-archive-merge-receipt-and-settings.md) 固定 Schema 8
  Receipt、幂等 Operation、可移植设置权威值和 NoOp 行为。
- [Archive Atomic Merge v1](../specifications/ARCHIVE_ATOMIC_MERGE_V1.md) 固定单事务、
  取消边界、来源 Revision、Undo、Tombstone、Chronicle 和提交后显示错误边界。
- Apply 重算 Plan 指纹，并在写事务内重验目标 Schema、完整数据库状态令牌、父对象映射、
  Tombstone marker/version、唯一名称和实时容量。
- staging 事实按 Plan 中稳定引用重新读取并核对语义摘要；Plan 后被替换或改变的输入在
  目标写入前拒绝。

## 单一事务内容

一次非 NoOp Merge 按一个目标 `DataChangeSet` 提交：

1. 新档案、共享角色身份、账号副本、身份别名和新档案 Undo 设置；
2. Gacha 当前事实、物品观察、手工获取和挑战观察；
3. Archive 保留的 Gacha／手工获取来源 Revision；
4. 经确认的 Tombstone 重引入和用户确认采用的白名单设置；
5. 必要 Undo 材料、`OperationHistory`、`OperationCommitResults`；
6. `ArchiveRestoreReceipts` 及来源→目标档案／账号映射。

任一步异常均由 SQLite 事务整体回滚。事务开始写入前可以取消；开始写入后不再读取普通
取消信号，最终只返回完整提交或完整回滚。

## 幂等、Undo 与 Chronicle

- Receipt 以 `OperationId` 唯一；应用重启后可通过 `FindReceiptAsync` 查询已提交结果。
  同一 Operation、包和 Plan 重试返回 `AlreadyCommitted`，不重复写事实或历史；同一
  Operation 被用于其他恢复时返回 Conflict。
- 再次为同一包和当前目标生成 Plan 时，全部事实已存在会得到 NoOp。NoOp 保存 Receipt
  和提交结果，但不制造空 ChangeSet。
- 只有既有父对象上的纯当前事实新增、没有来源 Revision／别名／设置／Tombstone 等
  不可撤销子项，且相关档案均允许保存完整 Undo 材料时，整个跨档案 Merge 才可撤销。
  一个跨档案 ChangeSet 在每个相关档案计数，但只提交和撤销一次。
- 当前事实取得目标本地版本令牌；来源 Revision 保留稳定 RevisionId 和历史 before/after，
  但绑定目标 Restore ChangeSet，不复制来源 ChangeSetId 或 Undo 资格。
- 当前事实写入后，事务显式失效受影响账号／档案的已发布 Chronicle generation，并清理
  对应缓存条目。来源 generation、epoch 和游标不导入；新 generation 仍由目标事实离线重建。

## Schema 8

v7→v8 在单一迁移事务中增加：

- `PortableApplicationSettings`：首版权威保存 `RequireUniqueArchiveNames`；
- `ArchiveRestoreReceipts`；
- `ArchiveRestoreReceiptArchives`；
- `ArchiveRestoreReceiptAccounts`。

v1～v7 均显式迁移到 v8。未来版本、错误 application ID、损坏库及迁移中途失败继续执行
既有“保留原库并拒绝写入”策略。项目尚未公开发布，因此只保证采用未来版本保护后的客户端
行为；不承诺旧内部构建能够安全回开 Schema 8。

## 自动化证据

Release 全量测试：`582/582 passed`，0 failed，0 skipped。

8C.3 新增覆盖包括：

- 普通 Apply、跨领域与跨档案一次 ChangeSet／一次 Undo；
- 同 Operation 重启重试、重新规划 NoOp 和持久化 Receipt 查询；
- Plan 后目标变化、staging 内容变化及提交前取消零写入；
- Tombstone 精确 marker/version 重引入；
- 可移植唯一名称／UndoLimit 设置同事务应用；
- 实时空间不足时父对象、事实和 Receipt 均不写入；
- 第二／第三领域、来源 Revision、Receipt 前后及提交前共九个故障注入点整体回滚；
- 当前事实采用新目标版本令牌，来源 Revision 保留历史版本内容；
- 受影响 Chronicle generation 失效且缓存条目清除。

适用宿主构建：

| 验证 | 结果 |
|---|---|
| MAUI Windows Release | Passed；0 warnings，0 errors |
| 独立 WinUI 3 UI-0 Release | Passed；0 warnings，0 errors |
| Android Release，`AndroidSdkDirectory=E:\AndroidSDK` | Passed；0 warnings，0 errors |
| CI | Not run；本地提交尚未推送 |
| Windows／Android 正式文件流程 | Not run；尚无 8C.5 产品入口 |
| 真实生产数据库 Apply | Not run；本轮只使用合成数据和临时数据库 |
| 真机 Archive 恢复／跨端往返 | Not run；属于 8C.5～8C.6 |

## 本地提交

| Commit | 内容 |
|---|---|
| `2008e56` | 固化 8C.3 原子 Merge、Receipt 与设置契约；版本推进到 0.8.12 / 8C.3 |
| `5d80afa` | Schema 8、应用设置权威存储、Archive Restore Receipt 表和迁移测试 |
| `22a3365` | 单事务跨领域 Merge、幂等 Receipt、Undo、Tombstone、容量、Chronicle 失效及故障注入测试 |

## 8C.4 最短交接

1. 先为 Replace 的明确选择范围、被移除事实／历史、目标 Tombstone 和旧跨档案 Undo
   引用给出可审查契约；不能把 Merge 的 Add-only 语义直接改成删除。
2. 决定提交前一致恢复点的位置、生命周期、容量和失败清理，再实现 Replace。
3. Replace 必须继续使用单一跨领域事务，并证明未选档案及共享身份下的其他副本不变。
4. 进程终止／重开、旧 Undo 引用、空间不足和受控恢复点是 8C.4 的退出条件；8C.3 的
   Receipt 不能替代 Replace 恢复点。
5. 正式 UI、系统 picker/saver、Android URI 生命周期和真实双向往返留在 8C.5～8C.6。
