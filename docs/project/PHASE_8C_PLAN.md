# Phase 8C 实施计划与首轮记录

状态：8C.0 contract accepted；8C.1 export foundation completed；8C.2 in progress

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
- 把新增长期兼容／不可逆语义集中在 ADR 0021；维护者已于 2026-10-23 接受。

### 8C.1：逻辑载荷、容器和一致导出

依赖 ADR 0021；契约已接受，按以下范围实施：

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
| Version | `Version.props` | 启动前 `0.8.8 / 8B.7`；8C.0 为 `0.8.9 / 8C.0`；8C.1 推进为 `0.8.10 / 8C.1` |
| 数据库 | `FurinaDatabase.CurrentSchemaVersion` | Schema 7；8C.0 不增加 Schema |
| 本地测试 | Release、`--no-restore` | 525/525 通过，0 skipped，2026-10-23 重跑 |
| CI push | run `39814452180` | 当前 HEAD completed / success |
| CI PR | run `39814460050` | Unit tests、Windows Release、Android Release APK 全部 success |
| UIGF | UIGF-org 当前规范 | Current v4.2；现有实现版本不变 |

规划文件原先记录 CI 尚在运行；实施启动时已复核为成功，因此更新为以上最终证据。

## 8C.0 交付

| 交付 | 状态 |
|---|---|
| ADR 0021 | Accepted 2026-10-23 |
| 22 表保真矩阵 | Review complete |
| Archive v1 内部实施规范 | Accepted；与 8C.1 实现同步 |
| 最小／完整／负例 | Review complete；正式 Writer／Reader 在自动化中生成并回读 |
| Version 里程碑 | `0.8.9 / 8C.0`；8C.1 开始后为 `0.8.10 / 8C.1` |
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
| P1 | ADR 0021、保真矩阵、容器与示例 | Completed / Accepted |
| P2 | Services 逻辑模型与版本派发 | Completed |
| P3 | Infrastructure Reader／Validator／Codec | Completed |
| P4 | 一致快照与 logical exporter | Completed |
| P5 | 自动化、三宿主适用构建、阶段记录 | Completed；设备与生产恢复不在本轮范围 |

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

## 8C.1 完成记录

- 版本为 `0.8.10 / 8C.1`；数据库仍为 Schema 7，没有生产数据库迁移。
- 正式 Writer 保留 catalog、标准与非标准 Gacha 当前事实、Gacha Revision、角色／武器观察、手工获取及其 Revision、挑战观察和白名单设置。
- Reader 在写库前执行路径、哈希、长度、版本、资源上限、父引用、跨 Dataset 数量及 UIGF／补充历史一一对应校验；UIGF 与 Gacha NDJSON 使用流式读取。
- Exporter 使用 SQLite Online Backup 创建 app-private 一致快照，正式 Reader 回读通过后才返回成功；成功、失败和取消均清理 session。
- Release 测试为 547/547；MAUI Windows、独立 WinUI、Android（`E:\AndroidSDK`）适用构建均为 0 警告、0 错误。
- CI 未运行：本轮提交尚未推送。Windows／Android 真实文件交互、真实生产库导出和跨端恢复均为 `Not run`；8C.1 没有产品 UI，也没有恢复实现，不能据此补造设备证据。

详细文件、保真范围和验证结果见 [8C.1 实施记录](PHASE_8C1_IMPLEMENTATION.md)。

## 8C.2～8C.3 最短交接

1. 8C.2 在 app 私有 staging 中读取同一 Archive，生成不写库的统一 RestorePlan。
2. Plan 绑定包指纹、目标 scope epoch／版本、档案／账号映射、Tombstone 命中、设置差异、资源估算和全部确认。
3. 8C.3 提取现有事务内 mutation helper，在一个 SQLite 事务中提交父对象、三个领域、Revision、目标 ChangeSet／Receipt 和必要设置。
4. 任一领域、Revision 或 Receipt 故障都整体回滚；提交后 UI／Chronicle 刷新失败只作为展示错误。
5. Merge 完成并通过故障注入前，不开始 Replace。

## 8C.2 实施边界

8C.2 只实现 `Inspect → Validate → Stage → Plan → Preview`，不执行 Apply：

- 系统文件流先复制到 app-private 临时文件并计算包 SHA-256；正式 Reader 完成版本、路径、长度、哈希、记录数和跨文件引用验证后，才形成私有索引。
- staging 使用内部 SQLite 作为有界索引实现，但不是公开格式、不是生产数据库镜像，也不能直接替换主库。
- 统一 RestorePlan 同时覆盖档案、账号、Gacha、角色／武器、挑战、Revision、空父对象和白名单设置；不得顺序拼接三个独立 Portable Plan 冒充统一预览。
- Plan 绑定包指纹、目标一致快照令牌、Schema、映射、Merge 模式、设置选择和全部确认。目标记录数相同不表示 Plan 仍有效。
- 档案名称继续 Ordinal 精确匹配；相似名称只提示并要求选择。多个精确同名目标必须手工映射。计划创建的同名档案按稳定编号重命名并报告。
- 已解析账号按规范自然身份映射；同 GUID 不同自然身份、同稳定事实／Revision ID 不同内容报告冲突。不同档案中的同一角色仍是独立业务副本。
- Unresolved 账号不按 UID、名称或占位字段猜测目标：新档案中可创建；映射到已有档案时如存在可能候选，必须显式选择创建或映射。
- 目标活动 Tombstone 对当前事实和会泄露已 purge 内容的 Revision 均产生绑定 marker/version 的确认项；8C.2 不停用 Tombstone。
- 未知可选 Dataset 只有在请求显式接受 partial restore 时才能形成可执行计划；未知必需 Dataset 和 future Archive major 仍由 Reader 拒绝。
- 默认保留目标设置；新档案采用来源 UndoLimit。用户选择应用来源设置时，唯一名称冲突必须在 Apply 前解决。
- 只要创建父对象／身份别名、携带来源 Revision、应用设置或需要 Tombstone 重引入，整个恢复不承诺普通 Undo。只有向既有父对象纯新增事实且可完整捕获材料时才可标记为可撤销候选。

完整契约见 [Archive RestorePlan v1](../specifications/ARCHIVE_RESTORE_PLAN_V1.md)。8C.2 不增加正式 UI，不访问真实生产库，不写 Receipt，也不修改 Schema 7。

## 明确不做

- 生产库 Restore、Replace 或主库文件切换；
- 云同步、ChangeJournal、WebDAV、服务端账号、E2EE、社区统计；
- 在线 Provider、UIAF 成就或未支持挑战模式；
- 完整 UI-1 或把 WinUI UI-0 接入真实数据库；
- 设备证据补造或使用私人数据库制作 fixture。
