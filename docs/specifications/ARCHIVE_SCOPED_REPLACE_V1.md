# Archive Scoped Replace v1

状态：Proposed 8C.4 implementation contract；未公开发布

依赖：Accepted ADR 0021；Proposed ADR 0023

## 目的

Replace 将用户明确选择的目标逻辑范围重建为 Archive 来源状态。它不是 Merge 的覆盖开关、
父实体 purge、普通 Undo 或主库文件恢复。

在 ADR 0023 被维护者接受前，本规范不能授权生产 Replace 写入。

## 范围模型

```text
ReplaceSelection
├─ Archive(source_archive_ref)
├─ Account(source_archive_ref, source_account_ref)
└─ Domain(source_archive_ref, source_account_ref, domain)
```

`domain` 只能是：

- `gacha`：当前 Gacha 与 Gacha Revision；
- `item-observation`：物品／角色／武器状态观察；
- `manual-acquisition`：手工获取当前事实与 Revision；
- `challenge-observation`：挑战观察历史。

选择必须至少一项、引用当前 staging、两两不重叠。Archive 选择覆盖其后代，Account 选择
覆盖该账号全部 Domain；同时给出祖先和后代是无效请求，不自动折叠。

## 前置验证

Replace Plan 复用 8C.2／8C.3 的包指纹、目标状态令牌、父对象映射、Tombstone、设置、
partial Dataset 和资源验证，并额外绑定：

- 规范化选择列表及其指纹；
- 每个范围由 Manifest 声明的来源完整性；
- 目标独有当前事实、Revision 和父实体；
- 将失效的目标 ChangeSet／Undo 材料；
- 候选恢复点预计字节、当前旧恢复点字节和磁盘峰值；
- 破坏性确认及所有 Tombstone marker/version。

来源缺失必需 Dataset、选择范围不完整、目标或 staging 变化、恢复点空间未知／不足、
重叠选择及父对象映射歧义均使 `CanApply=false`。

## Preview

每个范围至少显示：

| 项目 | 说明 |
|---|---|
| Add | 来源存在、目标不存在 |
| Replace | 稳定 ID 相同但可移植语义不同 |
| Keep | 稳定 ID 与内容相同 |
| Remove | 目标存在、来源不存在 |
| Reintroduce | 命中目标 Tombstone 且 marker/version 已确认 |
| Parent update/remove | 将更新的映射父属性及 Archive 范围内目标独有账号 |
| Revision add/replace/remove | 随领域精确替换的历史 |
| Undo invalidation | 将整体失效的目标 ChangeSet 数量 |
| Recovery peak | 主库、旧恢复点、新候选和预计增长的峰值 |

一个完全空的选择无效；一个来源内容为空但范围明确完整的选择是有效清空，仍要求
`DestructiveReplace` 确认。

## Apply

Apply 顺序属于同一个 SQLite 事务和同一个单写者边界：

1. 重验 Plan、目标、staging、Tombstone、设置和空间；
2. 在首次 mutation 前创建并校验候选 SQLite Online Backup；
3. 失效命中范围的旧 Undo 资格并删除其 UndoMaterial；
4. 删除选择范围的目标独有／不同事实与适用 Revision；
5. 更新或删除明确范围内父对象；
6. 写入来源当前事实、Revision、经确认 Tombstone 重引入和独立选择的设置；
7. 写一个不可 Undo 的 Replace ChangeSet、Receipt 和 RecoveryPoint 元数据；
8. 失效受影响 Chronicle generation；
9. 提交后有界清理旧恢复点和孤儿文件。

任一步在事务提交前失败，目标业务状态和 Receipt 均不改变。恢复点文件清理属于提交后的
幂等维护；失败不得把已提交 Replace 报告成未提交。

## 本地状态边界

- 来源 ChangeSet、Undo、Receipt、Tombstone、Chronicle 和本地版本令牌不迁移。
- 目标活动 Tombstone 不删除；Replace 删除本身不生成新 Tombstone。
- Replace 自身不进入普通 Undo。
- 被 Replace 失效的旧操作保留审计摘要，但失去 Undo 材料。
- 恢复点是设备本地 SQLite 一致副本，不进入 Archive、不跨端、不含额外凭据。
- 8C.4 不执行打开连接状态下的主库文件替换。

## 必测反例

1. Domain Replace 不改变同账号其他三个领域。
2. Account Replace 不删除同档案目标独有的其他账号。
3. Archive Replace 删除目标独有账号，但不改变其他档案及共享身份下的其他副本。
4. 空而完整的来源范围清空；缺失 Dataset 阻塞。
5. 目标独有事实和 Revision 全部出现在 Preview，不按遍历顺序静默覆盖。
6. Tombstone 未确认、Plan 陈旧、空间不足、取消和恢复点失败均零业务写入。
7. 第二／第三领域、Revision、父实体、Receipt 和提交前故障全部整体回滚。
8. 命中所选范围的跨档案旧 Undo 整体失效，未选事实保持。
9. 提交前进程终止后主库可打开且无 Receipt；提交后重启返回原 Receipt。
10. 新恢复点提交后旧点才过期；清理失败不影响已提交事实并可再次清理。
