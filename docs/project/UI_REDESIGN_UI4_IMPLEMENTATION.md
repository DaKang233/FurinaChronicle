# UI-4 管理闭环实施记录

日期：2026-11-13（Asia/Shanghai）。基线：`master 6a58b59`（UI-3 全部完成后）。

UI-4 目标（计划 §18）：日常操作完整可达；退出凭据不删档案；Undo 和删除提示正确。
Windows 使用独立 WinUI 3 宿主，Android 保留 MAUI；生产库/凭据迁移仍按 ADR 0020 在
UI-6 评审前不接入。本记录覆盖 WP-A1（组合根/壳/启动/概览/档案与账号）与 WP-A2
（启动失败修复/操作历史与撤销/设置）；备份恢复、登录通行证与 SToken 刷新为后续
子阶段，完成时追加。

## WP-A1：组合根、导航壳、启动、概览、档案与账号

提交：`a30494d`（Presentation/Management 状态机与测试）、`1e035b4`（Windows 宿主）。

- **组合根与端口**：`FurinaChronicle.Windows` 引入依赖注入，`WindowsComposition` 建立
  唯一组合根与生命周期；全应用共享**一个** `Ui1PreviewEnvironment`（隔离库 + 排他
  `session.lock`）。端口收敛为 `IFilePickerService`（窗口关联 Open/Save/Folder）、
  `IUiDispatcher`、`IArchiveSelectionStore`（隔离宿主本地 JSON，损坏退化为无选择）、
  `WindowsPaths`、`WindowsVersionInfo`、`IStartupProbe`。档案/账号全部用例、选择服务、
  `IArchiveOperationHistoryStore`/`IArchiveDomainHistoryStore`、`ManageGachaHistory`、
  Gacha 协调器等集中注册；组合根暂留在 Windows 项目并标注后续可抽共享。
- **导航壳**：`MainWindow` 重写为真实结构——主区概览、游戏历程（占位）、数据 · 抽卡、
  角色与武器/周期挑战（占位）；管理区档案与账号、操作历史；页脚登录账号/设置。
  未交付页禁用并标注“待交付”，不露出伪可用入口；窗口标题使用版本前缀
  （`FurinaChronicle · 0.10.0`），删除旧死代码；无全局当前账号。
- **启动**：启动页执行目录可写检查 → 隔离 DB 打开/Schema 校验；失败分类（目录不可写、
  数据库打开失败、Schema 不兼容、未知）+ 详情 + 重试；网络/元数据不参与启动闸门。
- **概览**：真实轻量聚合（档案/账号/身份待确认/抽卡记录数、逐档案最近活动、最近 5 条、
  领域覆盖说明、继续查看抽卡页），复用现有查询，不重建 Chronicle；选择只属于概览。
- **档案与账号**：左档案列表 + 右详情；创建/重命名/删除档案、添加/编辑/删除账号；
  显示身份状态与 `GetUnresolvedGameAccounts` 诊断；删除使用与 MAUI 一致的不可逆/
  级联范围原文；名称与 UID 原样交给服务（不 Trim）；已解析身份的 UID/区服只读；
  页面局部稳定 ID 选择，删除后回退并说明。
- 抽卡页改为注入复用共享环境；所有文件选择器走统一端口。

## WP-A2：启动失败修复、操作历史与撤销、设置

提交：`d880566`（状态机/守卫与测试）、`bc2b78f`（历史页、设置页、环境惰性化）。

- **D5 修复（高）**：WP-A1 在 `FURINA_UI1_DIRECTORY` 指向文件或不可写目录时启动崩溃
  （WER `0xc000027b`，底层 `0x800700B7`）。根因是 `OnLaunched` 急切构造隔离环境，
  `FurinaDatabase` 构造函数无条件 `CreateDirectory`，异常从 XAML 启动路径逃逸。
  修复：新增 `IsolatedStartupGuard` 在构造任何数据库对象前检查根不是文件、可创建、
  可写探针；`EnvironmentHolder` 惰性创建环境；启动闸门通过后才构建页面服务；畸形
  路径也不再从 `OnLaunched` 抛出。文件路径根与 ACL 禁写根均稳定显示“数据目录不可写”、
  详情含变量名与路径、重试可用、不创建数据库、进程不崩溃。
- **操作历史**：按档案加载最近 30 条；操作类型、提交时间（保留原始偏移）、范围
  （当前/跨 N 档案）、影响计数、可逆/不可逆、实时撤销资格（可撤销/未保留材料/材料已
  清理/已撤销/事实变化/永久删除/材料缺失/撤销不可再撤销/档案替换取代）；仅撤销最新
  可撤销项并确认；跨档案整体说明、安全失败、无热回切；失败原因分类与脱敏错误；
  UndoLimit 0～1000 显示/修改，越界在调用前拒绝；修订详情——抽卡修订展示
  before/after，领域修订展示实体/版本/快照原文并**明确标注不伪造字段级 before/after**。
- **设置**：外观跟随系统/浅/深并持久化到隔离宿主设置文件（启动时应用）；数据与存储
  显示隔离根/数据库/缓存路径与大小、清除图标缓存（不删事实）；历史 UndoLimit 与操作
  历史页共用同一服务与值；应用版本/阶段/提交、随包 `LICENSE.txt` 与
  `THIRD-PARTY-NOTICES.txt` 可读、诊断导出仅含版本/大小/安全声明（不含记录内容、
  凭据或完整用户路径）；无伪全量备份按钮。
- 导航启用“操作历史”“设置”；其余占位保持禁用。

## 验证

| 检查 | 结果 |
|---|---|
| WP-A1 Release 回归 | 840/840 通过（基线 822 + 18：管理/概览/启动/真实 SQLite） |
| WP-A2 Release 回归（Lead 复跑） | **914/914** 通过（基线 840 + 74：历史/设置/守卫/集成） |
| WinUI 构建/发布 | 每次 0 警告 0 错误；self-contained 发布含许可文件 |
| 版权头 | Pass |

Lead Windows UIA（真实发布产物 + 隔离库直查，共 71 项 Pass）：

- A1：u1 30 项（概览计数 3/4/71/0、禁用的占位导航、继续查看→抽卡页 UI-3 回归、档案
  创建/同名拒绝/重命名/删除级联与回退、账号增删改、数据库事实核对）；u3 4 项
  （Schema 99 不兼容阻塞、原库未改）；u4 5 项（身份待确认横幅）。
- A2：D5 文件路径根 6 项、ACL 禁写根 5 项（失败页/详情/重试/不建库/进程存活）；
  历史页 5 项（最近操作、UndoLimit=5、选择行加载修订）；设置页 12 项（深色主题与
  持久化、清缓存、许可/第三方声明、诊断文件安全）；u1（启用历史/设置后）30 项、
  u2 4 项、u3 4 项、u4 5 项回归。
- 证据：`wt/ui4-winui/artifacts/ui4-winui/lead-acceptance/uia/*.json` 与 `shots/`。

## 已知限制与后续

- 领域修订服务只提供实体/版本/快照 JSON，没有结构化字段级 diff；页面展示摘要并标注
  限制，不伪造 before/after。
- 撤销只支持最新可撤销项，无 Redo/热回切；跨档案操作整体撤销需确认。
- 窗口位置恢复与第二实例激活未实现（与生产单写者/切换方案一起评审）。
- 备份恢复（`.fca` 导出/Merge/Scoped Replace/恢复点）、登录通行证与 Windows 凭据
  存储、SToken 刷新为 UI-4 后续子阶段；生产库/凭据迁移在 UI-6 评审前不接入。
- Android 侧管理页已存在（MAUI），UI-4 的四目的地信息架构与设置组补齐纳入 Android
  UI 收口工作包。

## WP-A3a：备份与恢复（逻辑档案导出 + Merge）

提交：`d260557`（状态机与集成测试）、`ef5563c`（备份恢复页）。

- **导出**：使用正式 `ArchiveLogicalExporter` 生成完整逻辑档案 `.fca`（全部档案或所选档案），
  经窗口关联保存对话框；先写隔离宿主临时文件、导出器校验后发布到目标（跨卷复制兜底），
  失败/取消清理临时与空目标；结果含档案/账号/当前事实/修订计数。文案明确它是
  “逻辑档案（可合并/替换恢复），不是 SQLite 原始拷贝”。
- **打开与 Merge**：`SqliteArchiveRestoreInputStager` 校验格式/版本/截断/哈希/资源限制
  （失败不写库）；`PlanArchiveRestore` 生成 Merge 计划，预览包含来源、档案/账号映射、
  新增/复用/冲突/Tombstone、设置差异、缺失 Dataset≠清空、提交失败不改变原数据、无热回切；
  映射支持现有候选/新建唯一名，解析后自动重新规划；提交前校验包指纹、计划指纹与
  `CheckFreshnessAsync`，目标变化拒绝旧计划；`SqliteGachaAtomicChangeStore` 原子应用并返回
  Receipt/影响计数；重复提交按 Receipt 幂等。
- **错误分类**：无效包/版本/哈希/截断/资源限制/计划冲突/计划过期/清理空间不足分别提示；
  无效容器/JSON/截断映射为“所选文件不是有效的完整逻辑档案/文件被截断或损坏……未写入任何数据。”
  （D6 修复，提交 `cd73a1d`）。
- **恢复点**：只读摘要（时间/长度/Schema/校验状态），不提供热回切。
- **入口**：概览页与档案页“备份与恢复”，不新增导航项；页面只使用隔离库。
- **未完成**：Scoped Replace（范围选择/不可重叠校验/缺失 Dataset 不清空/Tombstone 独立确认/
  破坏性确认/Receipt）明确留待后续；页面无 Replace 控件。

## WP-A3b：登录账号、隔离凭据与 SToken 刷新

提交：`65c26f0`（状态机与测试）、`0be68ec`（DPAPI 凭据、登录页、SToken 刷新）。

- **隔离凭据存储**：`DpapiPassportCredentialStore` 使用 DPAPI CurrentUser + 固定 entropy 加密
  保存到 `<FURINA_UI1_DIRECTORY>/UI4/WinUI/credentials/credentials.bin`；实现
  `IPassportAccountStore`（刷新服务读取秘密的唯一边界）与 `IIsolatedCredentialStore`（安全元数据
  列表/保存/删除/清除/失效标记）；损坏/解密失败退化空列表并显示警告，不删除档案；删除/清除
  只删凭据，不动档案/账号/抽卡事实。文件与自检均无 SToken/MID 明文。
- **登录账号页**：启用页脚“登录账号”；列表显示标签、脱敏 MID、绑定 UID、更新时间、
  可用/失效；手动添加（SToken/MID 密码框 + 绑定 UID + 标签，本地格式校验）、删除/清除
  （确认文案明确不删档案）、失效/损坏提示、抽卡刷新入口；页面明确标注仅隔离宿主，
  生产凭据迁移待 UI-6 评审；任何状态/异常/日志/报告不含秘密。
- **SToken 刷新**：Windows 侧组合真实 `MiHoYoSTokenGachaUrlProvider`/`MiHoYoGachaLogClient`/
  `RefreshGachaRecords`（未改 `Ui1PreviewEnvironment`）；抽卡数据操作新增“通过登录账号刷新…”；
  目标账号与 Full 模式开始时快照；凭据必须绑定当前账号 UID；无凭据/UID 不匹配/凭据失效/
  网络失败/接口 UID 不一致分别给出明确中文原因且不写库；提交阶段不可取消；成功后按原范围刷新。
  **真实成功网络路径 Blocked**（无授权环境，成功逻辑由 fake executor 测试覆盖）。

## 更新后的验证

| 检查 | 结果 |
|---|---|
| WP-A3a Release 回归 | 919/919（914 + 5：导出全部/所选、空目标 Merge、冲突、计划过期、无效包） |
| WP-A3b Release 回归 | **958/958**（950 + 8：凭据/失效/UID/网络失败/Full 快照/秘密不外泄） |
| WinUI 构建/发布 | 每次 0 警告 0 错误；发布含许可文件 |
| DPAPI 自检 | PASS（加密文件无明文、脱敏、删除/清除生效、损坏退化警告） |
| 版权头 | Pass |

Lead Windows UIA（隔离库直查）：

- A3a：t1 导出 4 项（含再打开校验）、t2 空目标 Merge 4 项（数据库 68+3 恢复）、
  t3 无效包 4 项（D6 后明确文案、不写库）。
- A3b：登录账号页 12 项（隔离提示、空列表、添加、脱敏、凭据文件无明文、删除、
  档案事实不变）。
- 回归：u1 30 项（UI-4 管理闭环）、u2 4 项（D5 启动失败不崩溃）、UI-5 三页 11 项。
- 证据：`wt/ui4-winui/artifacts/ui4-winui/lead-acceptance/uia/*.json` 与 `shots/`。

## UI-4 剩余边界（进入 UI-6 评审）

- Scoped Replace 与恢复点切换未实现（只读摘要）；不提供热回切。
- SToken 成功刷新需授权环境实测；当前仅错误路径与 fake executor 覆盖。
- 窗口位置恢复、第二实例激活未实现，与生产单写者/切换方案一起评审。
- 生产库发现、备份、凭据迁移与默认入口切换必须在 UI-6 切换前单独评审并获维护者确认。
