# Phase 8B.4 实施契约与验证记录

状态：Accepted implementation contract

实施状态：In progress（2026-10-08）

基线：`feat/phase-8`，起始提交 `184f3cd97a421e72a41009d408e5007250bf7a28`

## 本轮边界

8B.4 将现有 Gacha 产品入口接到 ADR 0013 的原子 ChangeSet／Revision／Undo／
Tombstone／Portable Apply 基础。Q1～Q7 的首版产品选择由
[ADR 0014](../decisions/0014-phase-8b4-gacha-production-semantics.md) 固定。

本轮不建设通用 EventStore、Sync／ChangeJournal、完整 Archive、Chronicle 基础，
也不提前统一后续角色、武器或挑战领域。

## P0 基线事实

- 分支／HEAD：`feat/phase-8 @ 184f3cd97a421e72a41009d408e5007250bf7a28`。
- 开始时工作区干净，无维护者未提交修改。
- Release 单元测试基线：421／421 通过。
- 数据库：Schema 5；ADR 0013、8B.3 实施契约和 Portable v1 Accepted 文档有效。
- 页面：账号／档案管理位于 `ArchivePage`；Gacha 与管理页选择状态相互独立。

## 生产入口清单

| 入口 | 当前调用路径 | P0 结论／8B.4 目标 |
|---|---|---|
| UIGF 与小助手两模式 | `GachaPageViewModel` → `ImportUigfGachaRecords` → `SaveBatchAsync(PreserveExisting)` | 迁移到统一 Gacha 政策和原子提交；补空写 Revision，非空差异冲突 |
| SToken 刷新 | `GachaPageViewModel` → `RefreshGachaRecords` → `SaveBatchAsync` | 迁移到统一刷新政策；Tombstone 过滤并报告 |
| 手工 URL 刷新 | 同上，仅 URL 来源不同 | 官方响应仍为 `OfficialApi`，不是 `UserEntered` |
| Windows Web 缓存 | 缓存只提供 URL，随后同一官方分页 | 仅 Windows 可用；保存语义与其他刷新一致 |
| 小助手下载 | 下载 UIGF 后进入 UIGF 核心 | 显式导入，不是自动采集或重引入授权 |
| 兼容 JSON | `ImportGachaRecords` 已注册 DI，无当前页面调用 | 移出生产 DI 或接统一政策；不得保留可注入旁路 |
| Portable Apply | `IGachaPortableImportApplier` → `SqliteGachaAtomicChangeStore.ApplyAsync` | 已有 AddOnly 原子入口；补产品文件流程、容量和 Tombstone 预览 |
| 记录纠正／普通删除／Undo | 仅有内部原子端口和测试，无完整产品 UI | 增加 Gacha 专用最小命令与页面入口 |
| 账号删除 | `ArchivePageViewModel` → `DeleteGameAccount` → Repository `DeleteAsync` | 由范围 purge 替代父表 cascade |
| 档案删除 | `ArchivePageViewModel` → `DeletePlayerArchive` → Repository `DeleteAsync` | 由范围 purge 替代父表 cascade，保护幸存档案 |
| 通行证删除 | `UserPageViewModel` → Passport store | 只删除凭据，不删除 Gacha 档案；保持现状 |

低层 `IGachaRecordRepository.SaveBatchAsync` 当前仍是三条生产写入旁路；
`DeleteGameAccount` 和 `DeletePlayerArchive` 当前仍是两条父实体删除旁路。
8B.4 完成前必须由可维护的编译边界或架构测试将生产调用者清零。

## Q1～Q7 落地摘要

1. 刷新：抑制命中项、继续正常项、报告抑制数。
2. UIGF：允许补空并生成 Revision；不同非空事实冲突，不新增通用覆盖入口。
3. 新父实体／别名导入整体不承诺 Undo；已有账号纯事实新增按现有规则。
4. 人工纠正后的当前来源为 `UserEntered`；不伪造采集／导入时间。
5. 删除抑制按当前本地档案副本；同档案重建仍抑制已知事实，新档案独立。
6. Unresolved 使用本地副本最小 marker，不伪造自然身份。
7. Undo 显式重引入时恢复原活动 marker，并重验原 marker/version。

## 工作包状态

| 工作包 | 状态 | 说明 |
|---|---|---|
| P0 基线、矩阵、契约 | Completed | 本文和 ADR 0014；421／421 基线 |
| P1 原子前置条件 | Completed | expected-absent、版本 ABA、after-content、专用操作拒绝、Portable 容量确认；427／427 |
| P2 受控大文件暂存 | Partial | 已完成有限缓冲私有暂存、容器上限、可重开 lease 与兼容增量指纹；磁盘关联索引／分页 Plan 尚未完成 |
| P3 接管生产获取 | Pending | UIGF、刷新、小助手、兼容 JSON |
| P4 删除闭环 | Pending | 记录、账号、档案 purge 与 Tombstone |
| P5 纠正／历史／Undo UI | Pending | 最小产品入口，不做通用历史浏览器 |
| P6 Portable 文件产品流 | Pending | 双端 picker／preview／mapping／apply／export |
| P7 关闭旧旁路 | Pending | production SaveBatch／parent Delete caller = 0 |
| P8 双端真实验收 | Pending | 自动化、Windows、Android 真机与跨端文件分别记录 |

## 证据纪律

每个工作包完成后记录实际提交、自动化和平台证据。Build、CI、fixture、模拟器和
真机证据分别标注；未实际执行的项目写 `Not run` 或 `Blocked`。完整 P8 未满足前，
不得将整个 8B.4 标为 Completed。
