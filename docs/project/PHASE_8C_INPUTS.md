# Phase 8C 输入清单

状态：Consumed by Phase 8C.0；Phase 8B 已于 2026-10-23 关闭

## 可直接复用的基础

- 档案内账号副本与跨档案共享 `GameRoleIdentity`；业务事实始终归属本地 `GameAccountId`，Portable 通过自然身份映射，不复用来源本地账号 GUID。
- Gacha、角色／武器、挑战均有独立领域事实、查询和导出边界；Chronicle 只是可重建投影，不是导出事实源。
- `RecordProvenance`、发生／观察／抓取／导入时间、原偏移及各领域 `Completeness` 语义已经冻结；未知值不由当前时间补齐。
- Gacha 与两个领域 Portable 已有 Reader／Validator／Plan／原子 Apply／Receipt；8C 完整 Archive 应编排这些 Dataset，而不是把 SQLite Row 或 Chronicle Entry 序列化。
- DataChangeSet、Revision、有限 Undo、LocalOnlyTombstone、容量确认及父范围不可逆删除已有实现。Archive 恢复必须复用这些写入边界，不建立第二套 Upsert／EventStore。
- Schema 7 提供 Chronicle generation／epoch、严格游标和离线本地元数据；投影缓存可删除并从领域重建。

## 8C 需要冻结的产品契约

1. 完整 Archive 的 manifest、Dataset envelope 列表、每类版本、校验和及附件引用；校验和只表示损坏检测，不表示真实性。
2. 全量备份与分类导出的边界；UIGF／UIAF 等通用标准继续优先，Furina 格式只承载没有合适标准或需要完整保真的数据。
3. 恢复预览、档案名称映射、同名／近似名称提示、账号自然身份映射、冲突分类和用户选择；不得把预览和 Apply 合并成不可审查写入。
4. 原子恢复策略、临时空间估算、失败保留现库、应用中断恢复，以及 SQLite 一致备份；不得只复制活动 WAL 数据库主文件。
5. Portable 设置与设备设置的边界。Windows 路径、Android SAF URI、权限、窗口状态、SecureStorage key 和 DeviceId 不进入跨端设置。
6. 大型 RawSnapshot／附件的保留、去重、压缩和恢复策略；不将实时便签近重复响应无限永久保存。
7. 格式迁移政策：Archive 与各 Dataset 独立版本；旧版显式迁移，future major 默认拒绝；不与 AppVersion 绑定。

## 不应提前带入 8C 的内容

- 不在完整 Archive 工作中实现云同步、ChangeJournal、WebDAV、服务端账号、E2EE 或社区大规模统计。
- 不因未来《崩坏：星穹铁道》扩展而预先统一所有游戏领域模型；首个扩展可优先复用 UIGF 的抽卡入口。
- 不把 8B.7 隔离验收页当成正式备份／恢复 UI。8C 应在其真实 picker、Preview、Apply、export 证据上建立产品流程，并明确替换或隐藏该入口。
- 8B.7 的真实双向文件证据已经完成，但角色／武器或挑战 Portable 仍保持 internal / unpublished；公开支持承诺需要独立格式审查，不由 Phase 8B 关闭自动产生。

## 首批建议工作顺序

1. 以已关闭的 8B.7 设备证据为输入，冻结 Archive manifest 与 Dataset envelope。
2. 先实现无附件的本地完整备份、预览和原子恢复，并覆盖 Windows↔Android 文件闭环。
3. 再加入分类导出、Markdown 报告和可选附件；设置只包含 portable 子集。
4. 最后根据真实文件体积和恢复性能决定压缩、分卷、RawSnapshot 保留及未来传输接口，不提前实现云端。

8C.0 已在 [Phase 8C 实施计划](PHASE_8C_PLAN.md) 中记录实际基线，并形成 [ADR 0021](../decisions/0021-logical-archive-v1-fidelity.md)、[保真矩阵](../specifications/ARCHIVE_V1_FIDELITY_MATRIX.md)及样例。相关新语义已于 2026-10-23 接受，8C.1 已获准实施。
