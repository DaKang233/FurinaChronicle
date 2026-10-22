# Phase 8C 实施计划与首轮记录

状态：8C.0 Proposed contract ready；8C.1 Awaiting maintainer acceptance

启动日期：2026-10-23（Asia/Shanghai）

实施基线：`feat/phase-8 @ ebd364152303f0b12b5846d42640569d9258599c`

## 阶段目标

建立跨 Windows／Android 的逻辑 Archive、可审查恢复和真实文件闭环。Archive 保留已支持领域的事实与必要历史，不依赖 SQLite Row、Chronicle、网络或凭据。

Phase 8B 已关闭，本阶段不重开 8B.7。Windows 历史库升级前精确 Schema 未捕获的限制继续保留；UI-0 设备交互仍是独立工作，ADR 0020 不允许新 WinUI 技术小样接触生产数据。

## 首轮范围

### 8C.0：保真与契约

- 核对当前 HEAD、Version、Schema、CI、8B 退出记录和 Accepted 文档；
- 覆盖 Schema 7 全部 22 张表的数据保真矩阵；
- 给出 Archive v1 容器、逻辑父对象、Gacha 补充历史、设置、资源限制和版本派发提案；
- 给出最小、完整和负例样本；
- 把新增长期兼容／不可逆语义集中在 ADR 0021，等待维护者确认。

### 8C.1：逻辑载荷、容器和一致导出

依赖 ADR 0021 接受。确认后实施：

- Services 平台无关 Archive DTO、Reader/Writer 端口、诊断和摘要；
- Infrastructure ZIP Codec、Reader、Validator、版本派发；
- SQLite Online Backup 支持的一次跨领域一致快照导出；
- Gacha Revision、Unresolved 和不完整标准事实的 Archive-native 载荷；
- 空档案／空账号与可移植设置；
- 无数据库 Codec 往返、实库导出、并发一致性、资源上限和负例测试。

本轮不实现 RestorePlan、生产 Merge、Replace、正式 UI 或真实生产库恢复。

## 启动基线

| 项目 | 证据 | 结果 |
|---|---|---|
| 本地分支 | `git status`、`git rev-parse HEAD` | `feat/phase-8 @ ebd3641`，启动时 clean |
| 远端 | fetch 后比较 `origin/feat/phase-8` | 与本地 HEAD 相同 |
| Version | `Version.props` | 启动前 `0.8.8 / 8B.7`；8C.0 推进为 `0.8.9 / 8C.0` |
| 数据库 | `FurinaDatabase.CurrentSchemaVersion` | Schema 7；8C.0 不增加 Schema |
| 本地测试 | Release、`--no-restore` | 525/525 通过，0 skipped，2026-10-23 重跑 |
| CI push | run `39814452180` | 当前 HEAD completed / success |
| CI PR | run `39814460050` | Unit tests、Windows Release、Android Release APK 全部 success |
| UIGF | UIGF-org 当前规范 | Current v4.2；现有实现版本不变 |

规划文件原先记录 CI 尚在运行；实施启动时已复核为成功，因此更新为以上最终证据。

## 8C.0 交付

| 交付 | 状态 |
|---|---|
| Proposed ADR 0021 | Ready for decision |
| 22 表保真矩阵 | Review complete |
| Archive v1 候选规范 | Review complete |
| 最小／完整／负例 | Review complete；机器生成 ZIP 等待 8C.1 |
| Version 里程碑 | `0.8.9 / 8C.0` |
| 生产库／真实用户数据访问 | Not run；不在范围 |
| Windows／Android 设备流程 | Not run；8C.0 不产生设备产物 |

## 8C.1 实施边界

### 分层

- `FurinaChronicle.Services/Archives/Portable/`：逻辑 DTO、端口、Reader 结果、诊断和未来 Restore 协调端口。
- `FurinaChronicle.Infrastructure/Archives/Portable/`：ZIP、JSON、哈希、staging、一致快照 exporter。
- 现有 Gacha／HistoryDomains：复用领域 Codec 和规则，不复制第二套事实解析或写入语义。
- App／WinUI：8C.1 不接正式入口；WinUI 技术小样继续与生产库隔离。

### 一致导出

建议生命周期：

1. 初始化并只读检查生产数据库；
2. 使用 SQLite Online Backup 创建 session 私有一致副本；
3. 从副本读取 catalog、三个领域事实／历史和设置；
4. 有界／流式写入 Archive；
5. 通过正式 Reader 重新验证输出；
6. 关闭连接并清理临时 SQLite、WAL／SHM 和未完成包；
7. 把已验证文件交给平台 saver。

生产 SQLite 文件和临时 SQLite 快照都不进入公开 ZIP。

### 后续恢复接口约束

8C.1 可以定义但不调用以下边界：

```text
IArchivePackageReader
IArchivePackageValidator
IArchiveExportSnapshotFactory
IArchiveLogicalExporter
IArchiveRestorePlanner       // 8C.2 实现
IArchiveAtomicRestorer       // 8C.3 实现
```

`IArchiveAtomicRestorer` 必须代表一个跨领域提交，禁止其实现顺序调用三个独立事务。

## 工作包

| 工作包 | 内容 | 状态 |
|---|---|---|
| P0 | 基线、文档、22 表盘点、UIGF 核对 | Completed |
| P1 | ADR 0021、保真矩阵、容器与示例 | Awaiting maintainer acceptance |
| P2 | Services 逻辑模型与版本派发 | Blocked by P1 |
| P3 | Infrastructure Reader／Validator／Codec | Blocked by P1 |
| P4 | 一致快照与 logical exporter | Blocked by P1 |
| P5 | 自动化、三宿主适用构建、阶段记录 | Pending 8C.1 implementation |

## 验收

8C.1 完成前至少验证：

- 无 SQLite 的最小／完整 Codec 往返；
- Schema、跨文件引用、哈希、路径和版本负例；
- 空档案、空账号、Unresolved、不完整 Gacha 和 Revision 无损；
- 同 UID 跨档案副本不合并；
- 并发写入期间一次导出仍来自同一快照；
- Chronicle 不可用、网络不可用时导出仍成立；
- 资源超限和取消不留下临时文件或伪成功；
- 525 项既有测试、版本脚本、版权检查、diff 检查；
- MAUI Windows、独立 WinUI 和 Android 适用构建。

8C.1 只可称为“导出基础完成”，不能称为 Archive 恢复或跨端闭环完成。

## 8C.2～8C.3 最短交接

1. 8C.2 在 app 私有 staging 中读取同一 Archive，生成不写库的统一 RestorePlan。
2. Plan 绑定包指纹、目标 scope epoch／版本、档案／账号映射、Tombstone 命中、设置差异、资源估算和全部确认。
3. 8C.3 提取现有事务内 mutation helper，在一个 SQLite 事务中提交父对象、三个领域、Revision、目标 ChangeSet／Receipt 和必要设置。
4. 任一领域、Revision 或 Receipt 故障都整体回滚；提交后 UI／Chronicle 刷新失败只作为展示错误。
5. Merge 完成并通过故障注入前，不开始 Replace。

## 明确不做

- 生产库 Restore、Replace 或主库文件切换；
- 云同步、ChangeJournal、WebDAV、服务端账号、E2EE、社区统计；
- 在线 Provider、UIAF 成就或未支持挑战模式；
- 完整 UI-1 或把 WinUI UI-0 接入真实数据库；
- 设备证据补造或使用私人数据库制作 fixture。
