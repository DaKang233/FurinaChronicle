# Phase 8C.7 退出评审

2026-11-09。**Phase 8C 功能验收关闭，8C.7 交接完成。** 设备工作流状态为 `Device workflow verified`。资源测量仍有覆盖限制，详见下文。

里程碑 `0.8.16 / 8C.7`。业务数据库 Schema 11，内部 Archive `1.0`。本地验证基于 `feat/phase-8` 的 `5e2a4a5` 及当时的工作区，构建版本为 `0.8.16.215`，标记 `dirty=true`。Windows 隔离宿主依赖工作区已有的存储隔离修改。证据按实际产物指纹绑定。

## 功能退出条件

| 条件 | 结果与证据 |
|---|---|
| 实现、格式与保真 | 8C.0～8C.6 完成。Archive 1.0 支持边界、迁移与恢复说明汇总于[实施与交接](PHASE_8C_IMPLEMENTATION.md) |
| 双端正式文件入口 | 8C.6 Windows 与 Android 实际选择文件、Preview、确认、取消、Merge、NoOp、重开、再次导出均有设备记录 |
| 独立双向往返 | 两份独立来源通过严格比较。修复后 25 个文件大小及 SHA-256 独立核验吻合，8 组关键语义比较复核均为 Equivalent |
| 迁移和 Unresolved 保真 | Windows 受控副本 Schema 10→11 后完整性、外键和归属均通过。两个方向的新种子首跳均保留 Unresolved 原因 |
| Scoped Replace 与 Tombstone | 双端正式受控 Replace、取消和未选副本保全已有证据。Windows 与 Android 的受控重引入均通过 |
| R08 提交前中断 | Windows TerminateProcess 与 Android 真机 force-stop 均在 `BEFORE_COMMIT` 后实际终止写入进程。重开后原状态令牌与记录一致，未提交 Receipt 缺席，孤立恢复点各清理一份 |
| R07 与 R08 提交后重开 | 两端均在 `AFTER_COMMIT` 后强制终止。新进程按原 OperationId 找到同一 Applied Receipt，包及 Plan 指纹和映射一致，恢复点有效。重放返回 AlreadyCommitted，数据库状态令牌一致 |
| ENV-W Windows 存储隔离 | 8C.7 新构建在独立目录实际打开用户页，确认无已登录通行证。正式备份与恢复页初始化了隔离 Schema 11 数据库，app-data 与 cache 也在独立目录。旧报告中的凭据隔离限制作为历史记录保留 |
| 工程验证 | 阶段关闭提交 `f4988de` 的干净检出有 616 项 Release 测试通过。另一次包含未提交 UI 改动的本地检出有 632 项通过，新增 16 项来自 UI 测试。两次均无失败或跳过。Windows 控制台探针、Windows MAUI x64 Release 隔离构建及 Android ARM64 Debug 探针均构建通过，0 警告、0 错误。版权头及 Git 空白差异检查通过 |

R07/R08 的控制台与设备探针复用实际包读取、Preview 规划、Scoped Replace 事务和 Receipt 查询。在提交前使用现有故障注入接口停在事务内，由外部终止进程。正式页面的文件交互证据来自 8C.6 设备报告。

## 证据索引

原始报告保存在维护者工作区的 `phase8c6-artifacts` 目录。文件均为合成验收资料。

| 目录或文件 | 用途 |
|---|---|
| `run-20261028-0115/03-final-report.md` | 初轮双端流程与受控 Replace |
| `run-20261030-2358/05-retest-final-report.md` | 修复后保真、迁移、双端操作与 R06 资源采样 |
| `run-20261030-2358/rerun-214/06-rerun-214-report.md` | 固定 Build 214 的 Android 重跑、双向文件与实际 APK 指纹 |
| `review-20261031/REVIEW.md` | 25 份文件及 8 组关键严格比较的独立复核 |
| `exit-20261109/windows-probe.log`、`android-probe.log` | R07、R08 的操作标识、PID、状态令牌、指纹与 Receipt |
| `exit-20261109/windows-storage-check.log` | Windows 环境隔离与实际页面启动 |
| `exit-20261109/artifact-identity.txt`、`android-package.log` | 8C.7 实际验证产物绑定 |
| `exit-20261109/tests/phase8c7.trx` | 混合工作区 632 项 Release 回归结果 |

本轮 Windows MAUI DLL SHA-256 为 `edd8236457c841d6efed6e6c3437d51b3bf017934748fe9e6ec914f04628c98a`。Android 真机为 Android 14／arm64-v8a，独立包 `com.companyname.furinachronicle.validation.phase8c7probe`，versionCode 215，APK SHA-256 为 `1ec633a1083887a8c78db20fbc6974beaa6027756aea45ab01f1f42a7b91cf2e`。

## 留项与边界

- Android 正式流程的 app-private staging 峰值仍为 `Not measured`。Preview 与 Apply 的内存使用来自合并采样窗口，操作时间部分为墙钟估计。按补测指南保留测量边界。设备工作流结论覆盖功能，完整分阶段性能测量仍留项。
- 8C.6 初始预置 APK 的旧字节指纹无法复现。功能证据绑定该轮实际重建并重跑的 Build 214 APK。8C.7 探针另有自己的 APK 指纹。
- 本轮 CI 状态为本地验证。当前工作区的 GitHub Actions 与正式 Android Release 产物未重验。8C.6 报告中的 Android 正式页面来自独立 Release 验收包。
- 主库恢复点回切、外部公开格式发布与 UI 路线的验收继续独立管理。Phase 8B Windows 历史升级前精确 Schema 未捕获的限制继续保留。

Phase 9 可从角色解析、认证与首个 Provider 纵切开始，沿用当前 Archive 和 Schema 边界。数据来源及采集契约仍按工作包评审。
