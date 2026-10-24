# Phase 8C.4 分层 Replace 与本机恢复点实施记录

状态：Implementation complete；automated verification complete；OS process-kill / device acceptance not run

日期：2026-10-25（Asia/Shanghai）

版本：`0.8.13 / 8C.4`

数据库：Schema 9

## 结论

8C.4 已按 Accepted [ADR 0023](../decisions/0023-scoped-archive-replace-and-recovery-point.md)
实现内部 Archive Scoped Replace。用户必须明确选择 Archive、Account 或 Domain 范围；
Plan 与 Preview 会列出 Add、Keep、Replace、Remove、Revision、父对象删除、旧 Undo
失效和恢复点空间，再经破坏性确认进入与 8C.3 Merge 相同的单写者、单 SQLite 事务。

本记录只关闭内部工程纵切。应用仍没有正式备份／恢复产品入口，也没有实现把恢复点重新
切换为主库的流程。恢复点是 Replace 前的设备本地一致 SQLite 副本，不是公开 Archive，
不跨端，也不能用作绕过正式 Reader／Validator 的导入格式。

## 已实现范围

- `ArchiveRestoreMode.Replace` 与 Archive／Account／Domain 三层显式选择；
- Gacha、物品观察、手工获取及其 Revision、挑战观察四个领域的精确重建；
- 空但由 Manifest 完整声明的范围可清空；缺失 Dataset、重叠范围和映射歧义阻塞；
- Archive 范围可删除来源中不存在的目标账号，但不删除共享角色身份和其他档案副本；
- 目标活动 Tombstone 保留；重引入仍需绑定 marker/version 的显式确认；
- 命中范围的旧可撤销 ChangeSet 整体失效，审计摘要保留，Undo 材料删除；
- Replace 自身不可通过普通 Undo 撤销；
- 每个数据库只保留一个当前恢复点，下一次成功 Replace 提交后才淘汰旧点；
- 恢复点按长度、SHA-256、`application_id`、Schema 和 `quick_check` 校验；
- 孤儿候选文件可幂等清理；失败的新 Replace 不覆盖上一有效恢复点；
- Receipt 持久化恢复模式和 RecoveryPointId，账号映射在账号被删除后仍可用于审计。

## 原子边界与失败语义

Apply 在首次业务 mutation 前通过 SQLite Online Backup 创建候选恢复点，并把恢复点预计
大小纳入实时空间检查。随后在同一事务内失效旧 Undo、更新／删除／写入事实和 Revision、
处理父对象、设置、Tombstone、Chronicle 失效、ChangeSet、Receipt 及恢复点元数据。

事务提交前出现目标变化、staging 变化、空间不足、取消、恢复点校验失败或故障注入时，
业务事实和 Receipt 均不改变，候选恢复点文件被删除。提交成功后，旧恢复点文件和孤儿
文件只进行可重试的有界清理；清理失败不能把已提交 Replace 报告成失败。

当前只支持恢复点创建、查询、验证和孤儿清理。未来启用“恢复此恢复点”时，必须另行处理
数据库连接关闭、WAL／SHM、同卷原子切换、回滚和恢复点元数据重建；8C.4 不进行热文件
替换。

## Schema 9

v8→v9 在单一迁移事务中：

- 为 `ArchiveRestoreReceipts` 增加 `Mode` 和 `RecoveryPointId`；
- 重建 `ArchiveRestoreReceiptAccounts`，移除对已删除目标账号的外键，同时保留档案和
  共享身份引用约束；
- 新增 `ArchiveReplaceRecoveryPoints` 和“每库一个当前点”唯一索引；
- 新增 `ArchiveReplace` 操作种类和 `SupersededByArchiveReplace` Undo 失效原因。

v1～v8 均显式迁移到 v9。迁移保留现有事实、设置、Receipt 和映射；不会为历史操作虚构
恢复点。未来版本、错误 application ID、损坏库和迁移失败继续保留原库并拒绝写入。

## 自动化证据

Release 全量测试：`594/594 passed`，0 failed，0 skipped。

其中 Restore 定向测试 `39/39 passed`，覆盖：

- Domain／Account／Archive Replace 的范围隔离；
- 四领域当前事实和 Revision 的更新、删除与保留；
- 空且完整档案清空，缺失 Dataset 阻塞；
- 目标独有账号删除、共享身份和其他档案副本保留；
- 目标 Tombstone、破坏性确认、陈旧 Plan 和幂等 Operation；
- 旧 Undo 失效、Replace 自身不可撤销；
- 恢复点容量前置失败、创建校验、轮换和孤儿清理；
- 第二次 Replace 失败时保留上一有效恢复点；
- 故障注入整体回滚；
- 重新打开同一数据库后可查询原 Receipt 和有效当前恢复点；
- v1～v8→v9 迁移、失败回滚、重复初始化和外键完整性。

自动化故障注入证明事务回滚与重开查询，但本轮没有执行操作系统级强制终止进程测试，
不得把它表述为已取得真实断电证据。

## 构建与设备状态

| 验证 | 状态 |
|---|---|
| Release 全量测试 | Passed；594/594 |
| MAUI Windows Release | Passed；0 warnings，0 errors |
| 独立 WinUI 3 UI-0 Release | Passed；0 warnings，0 errors |
| Android Release，`AndroidSdkDirectory=E:\AndroidSDK` | Passed；0 warnings，0 errors |
| CI | Not run；本地提交尚未推送 |
| 操作系统级进程终止／重开 | Not run |
| 正式 Windows／Android 文件流程 | Not run；尚无 8C.5 产品入口 |
| 真实生产数据库 Replace | Not run；只使用合成数据和临时数据库 |
| 主库恢复点回切 | Not implemented；需独立契约与实现 |

## 本地提交

| Commit | 内容 |
|---|---|
| `f8963d0` | 接受 ADR 0023 与 Archive Scoped Replace v1 |
| `48e727e` | Schema 9、恢复点元数据和历史 Receipt 映射迁移 |
| `c966c8e` | 显式 Replace 范围、Preview、完整性和资源规划 |
| `acf7523` | 单事务 Replace、删除、旧 Undo 失效和恢复点生命周期 |
| `89dabba` | 空档案清空与数据库重开边界测试 |
| `669a539` | Schema 9 历史迁移夹具和快照断言回归修正 |

## 8C.5 最短交接

1. 为现有 Export、Merge 和 Replace 建立正式产品流程，但继续复用同一个 Reader、
   RestorePlan 和 AtomicRestorer，不在 UI 建第二套写入路径。
2. Windows 和 Android 都必须经过系统文件选择／保存边界；Android 持久 URI、临时
   staging 和取消／后台恢复要单独验证。
3. UI 必须先显示完整 Preview 和确认项，再允许 Apply；处理中禁用冲突操作，重启后按
   Receipt 查询结果，不能因页面失败重复提交。
4. 主库恢复点回切不是普通 Archive Apply。若 8C.5 需要开放，必须先补独立 ADR 和
   关闭连接／原子切换方案；否则只显示恢复点状态，不提供恢复按钮。
5. 8C.5～8C.6 需取得 Windows↔Android 实际文件往返、真实进程终止／重开和受控数据库
   副本证据，不能继承 8C.4 自动化结论。
