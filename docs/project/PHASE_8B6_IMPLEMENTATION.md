# Phase 8B.6 实施契约与验证记录

状态：Accepted implementation contract

实施状态：In progress（P0 completed）

基线：`feat/phase-8 @ c38d6e17d92572cdb02962594b295c2390fbd90e`

## 目标

消费已经独立成立的 Gacha、手工获取、角色／武器状态观察和首批深境螺旋事实，生成确定、只读、可重建的 Chronicle 候选，经版本化 Profile 提供账号／档案查询、稳定分页和服务级可读报告。

Chronicle 不是领域事实来源。没有注册 Chronicle 或删除其缓存时，领域查询、历史、Markdown、UIGF／Portable 和重导入必须继续工作。

## 已冻结首版语义

- [ADR 0018](../decisions/0018-chronicle-generation-and-evidence.md)：EntryId、来源／Derived 分离、Undated、有界不可变 generation、游标生命周期、安全获取证据和状态变化开关。
- [ADR 0019](../decisions/0019-spiral-abyss-chronicle-completion.md)：深境螺旋目标末关完成证据、每期单一完成候选和未知时间退化。
- 既有 ADR 0009、0010、0011、0016、0017 继续有效，不重开 Week、观察回返或领域 Portable 历史范围。

## 实际输入与失效矩阵

| 输入／操作 | Chronicle 消费 | 写入后的权威动作 |
|---|---|---|
| Gacha 当前有效事实 | 五星、角色／武器获取、本地范围里程碑与可证明间隔 | 新增推进 source epoch；纠正／删除推进 destructive epoch |
| 手工获取当前有效事实 | 角色／武器获取；与 Gacha 不按近似时间自动合并 | 新增推进 source epoch；纠正／删除推进 destructive epoch |
| 角色／武器不可变观察 | 可选观察与相邻可信观察间 Derived 变化 | 新观察推进 source epoch |
| 深境螺旋不可变观察 | Challenge 观察；每期最多一个 Main 完成候选 | 新观察推进 source epoch |
| Undo | 只投影撤销后的当前有效事实 | 推进 destructive epoch，旧游标失效 |
| 不可逆事实删除 | 不得从 Revision 或缓存恢复 | 同事务清除相关缓存并推进 destructive epoch |
| 账号／档案 purge | 不得保留已清具体投影 | 同事务清除 scope 缓存并推进 destructive epoch |
| Portable Apply | 通过现有原子领域／Gacha写入 | 依实际新增或覆盖分类推进 epoch |
| NoOp／OperationId 重试 | 不改变候选 | 不推进 epoch |

## 真实数据核验

维护者提供的 Snap Hutao 导出目录包含两组深境螺旋、幻想真境剧诗和幽境危战 JSON。P0 只读取文件结构与数组形状，不把 UID、队伍、成绩或其他个人数据写入仓库、日志或 fixture。实际结构确认深境螺旋具有期次、楼层、间、星数和结算时间；生产 Contributor 仍只消费 Furina 已规范化的深境螺旋领域，其他两种模式不在 8B.6 产品化。

## 工作包状态

| 工作包 | 状态 | 说明 |
|---|---|---|
| P0 契约、基线、反例 | Completed | Release 493／493 通过；ADR 0018／0019 冻结 |
| P1 纯模型、证据、Profile | Pending | 不依赖 SQLite／MAUI／网络 |
| P2 Gacha／手工获取 | Pending | 从当前有效 SQLite 事实投影 |
| P3 状态观察／差异 | Pending | 不确定区间与显式开关 |
| P4 深境螺旋／成就 fixture | Pending | 生产仅深境螺旋；成就只用虚构输入 |
| P5 generation／游标 | Pending | 有界一致读取、原子发布与稳定分页 |
| P6 写入失效闭环 | Pending | 生产入口、Undo、永久删除、purge |
| P7 综合实库与报告 | Pending | 重建、故障、性能与领域自治 |
| P8 版本、文档、构建 | Pending | 自动化与设备证据分离 |

## 明确不属于本轮

完整 Chronicle 页面、在线角色／挑战采集、真实成就存储和目录、剧诗／幽境生产 Contributor、完整 Archive、Sync／ChangeJournal、通用 EventStore 和服务端。

## 验证纪律

自动测试、平台编译和宿主性能数据分别记录；没有 UI 或真机交互时记为 `Not run`。真实 Hutao JSON 只作本地结构核验，不进入提交。P7 必须提供从实际 SQLite 领域用例开始的可复现服务级报告，而不能只证明 DI 注册或直接填充投影表。
