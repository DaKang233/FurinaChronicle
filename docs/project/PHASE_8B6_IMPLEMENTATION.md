# Phase 8B.6 实施契约与验证记录

状态：Accepted implementation contract

实施状态：Implementation completed；平台构建已验证，产品 UI／真机交互不适用

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
| P1 纯模型、证据、Profile | Completed | Core 纯模型；确定 EntryId、时间退化、Profile／状态开关 |
| P2 Gacha／手工获取 | Completed | 当前有效事实；基础来源与派生注释分离；无覆盖不声明真实首次／ExactPity |
| P3 状态观察／差异 | Completed | 只比较双方覆盖字段；Partial 缺项不造删除；变化为不确定区间 |
| P4 深境螺旋／成就 fixture | Completed | 生产仅深境螺旋；成就仅虚构重要目录输入 |
| P5 generation／游标 | Completed | Schema 7；24 小时、每 scope 最多两代；严格 scope／filter／Profile 游标 |
| P6 写入失效闭环 | Completed | 事实表事务触发器覆盖新增／修改／删除；Undo、父范围删除和 purge 受控失效 |
| P7 综合实库与报告 | Completed | SQLite→Contributor→Profile→Markdown；取消不发布半代；100k 宿主压力通过 |
| P8 版本、文档、构建 | Completed | `0.8.7 / 8B.6`；Release 测试及双平台构建记录见下 |

## 实际实现边界

- 当前主库为 Schema 7；`ChronicleScopeStates` 是最小权威 epoch，`ChronicleGenerations`／`ChronicleEntries` 是可删除缓存，不进入任何 Portable。档案 scope 的规范账号选择集合有独立哈希，不会把子集 generation 误用于全档案查询。
- generation 构建按 500 条读取来源，单账号来源事实和单 scope 投影各有 250,000 条硬上限；超过时明确失败且不发布半代。
- 同一 generation 采用 `Timed → Undated`、`SortAtUtc DESC, EntryId DESC`；区间筛选按 UTC 半开窗口重叠判断。
- 新增事实推进 source epoch，新查询重建而旧游标继续使用不可变旧代；纠正、普通／不可逆删除、Undo 与父范围 purge 推进 destructive epoch，旧游标返回 `CursorInvalidated`。
- generation 最多保留当前与前一代、生命周期 24 小时；缓存清除／回收返回 `CursorExpired`。损坏或构建异常不会改变领域事实。
- `IChronicleQueryService` 提供账号／档案 scope、Profile、筛选、重建、清缓存与 Markdown。没有 Chronicle 页面，因而本轮没有用户界面或真机交互验收项。
- 自动重建仅批量读取已有本地 Gacha 元数据数据库，不调用会触发网络刷新的 Provider；已有缓存可恢复稳定 item ID 的角色／武器类型和星级，缺失时按安全 Unknown 退化。
- 当前成就贡献器仅消费调用方提供的虚构/未来输入，没有成就数据库、UIAF 或真实重要目录。幻想真境剧诗、幽境危战没有生产 Contributor。

## 自动化与宿主证据

- 纯规则覆盖稳定 EntryId、Instant／Interval／UncertainInterval／Undated、同秒／聚合记录、Unknown／Ambiguous、安全本地里程碑、状态覆盖边界和深渊每期单一完成节点。
- SQLite 集成覆盖新增后旧游标稳定、新查询换代；纠正／Undo／不可逆删除失效；跨 filter 拒绝；清缓存过期；跨档案隔离；取消无半代。
- 综合用例从生产 Gacha、物品观察、深境螺旋事务写入口开始，完成 Profile 查询和 Markdown 输出，而非直接填投影表。
- 显式 100,000 条合成 Gacha 宿主压力：测试通过；55.49 秒（含编译、测试宿主和 fixture 写入），监测到的 `dotnet test` 进程峰值工作集 137.3 MiB。该数据不是 Windows／Android 设备 UI 指标。
- 压力反例发现并修复序号证据引用 O(n²) 增长；现在每条只保留首条／当前边界引用。

最终验证（2026-10-19，Windows 开发宿主）：

- `dotnet test ... --configuration Release --no-restore`：517／517 通过；
- `build/Verify-Versioning.ps1`：通过；
- `git diff --check`：通过；
- Windows `net10.0-windows10.0.19041.0` Release：通过，0 警告／0 错误；
- Android `net10.0-android` Release，`AndroidSdkDirectory=E:\AndroidSDK`：通过，0 警告／0 错误；
- 版权头检查：通过。

未运行／延期：真实 Windows Schema 6 应用数据库升级、Android 应用创建 Schema 7、真机 UI、跨端新领域文件往返。这些属于 8B.7 的设备/文件证据；自动迁移测试和双平台编译不冒充实际应用验收。由于本轮没有 Chronicle 产品页面，不存在可达的 Chronicle UI 人工测试步骤。

## 8B.7 交接

- 验证 Windows 现有 Schema 6 数据库升级到 Schema 7，以及 Android 新建 Schema 7 数据库；自动迁移测试不能替代真实应用数据库证据。
- 为角色／武器与挑战 Portable 提供实际文件入口或受控 harness，完成 Windows↔Android 语义往返；格式仍为 internal / unpublished。
- 若未来增加 Chronicle 页面，另行验证真实 UI 的分页、筛选、缓存重建和本地化文本；8B.6 不虚构该证据。
- 继续验证禁用 Chronicle 注册后领域 Query／History／Markdown／Portable；架构上没有反向依赖，现有回归保持通过。

## 明确不属于本轮

完整 Chronicle 页面、在线角色／挑战采集、真实成就存储和目录、剧诗／幽境生产 Contributor、完整 Archive、Sync／ChangeJournal、通用 EventStore 和服务端。

## 验证纪律

自动测试、平台编译和宿主性能数据分别记录；没有 UI 或真机交互时记为 `Not run`。真实 Hutao JSON 只作本地结构核验，不进入提交。P7 必须提供从实际 SQLite 领域用例开始的可复现服务级报告，而不能只证明 DI 注册或直接填充投影表。
