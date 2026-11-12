# UI-5 领域页实施记录

日期：2026-11-13（Asia/Shanghai）。基线：`master`（UI-4 WP-A2 合并后）。

UI-5 目标（计划 §11、§18）：Chronicle、角色与武器、周期挑战等新增领域页，各自独立
查询/历史/导出/文件入口，保留证据边界；Android 按 ADR 0020 继续使用 MAUI。

本记录覆盖 **UI-5-1：Chronicle 展示层 + MAUI 页面**（提交 `c07e7b7`、`8eb61b0`）。
角色与武器/周期挑战的 WinUI 页与 Android 四目的地导航收口为后续子阶段，完成时追加。

## UI-5-1：Chronicle 游戏历程

### 展示层（`FurinaChronicle.Presentation/Chronicle/`，平台无关）

- `ChronicleDisplayText`：7 个 TitleKey、10 个 EvidenceReasonKey、6 个 MilestoneKey
  全部中文映射；枚举标签（来源/派生/置信/完整度/依据状态与范围/领域/类型/时间线/
  排序依据/挑战模式/区服）。未知键回退“未能识别的历程描述 / 依据说明不可用”，
  未知里程碑不显示，未知枚举显示“未知”；测试用真实 Contributor 产物断言覆盖且不出现
  `chronicle.` 裸键。
- `ChronicleTimeDisplay`：Occurred 优先并保留原始偏移；仅 Observed 显示“观察于”、
  仅 Fetched 显示“采集于”，明确不是发生时间；全缺显示“未注明时间”，不使用导入/
  当前时间替代；支持确定/不确定区间与秒～周精度，排序依据单列；展示层不引入
  ImportedAt。
- `ChroniclePageState`/`ChronicleQueryController`：首屏/加载更多、键集游标追加去重、
  GenerationId 变化拒绝混代、`CursorInvalidated` 清空并要求显式重载、`CursorExpired`
  保留内容但阻断分页、InvalidCursor/版本不匹配、传输错误、四种空态（无档案/无账号/
  无数据/筛选为空）；新查询取消旧请求并做代数校验。
- `ChronicleQueryOptions`：Profile/筛选/时间线/颗粒度与相对时间范围（显式 now/时区）；
  `ChronicleMarkdownReport`：本地化 Markdown（按月分组、Undated 区、标注不可重新
  导入），不把键名当报告。

### MAUI 页面与入口

- `ChroniclePage` + `ChroniclePageViewModel` 接入正式 `IChronicleQueryService`/DI；
  列表项使用 `ChronicleEntryDisplay`（标题/时间/详情），宽屏报告使用
  `ChronicleMarkdownReport`；物品名称经元数据服务解析，缺图降级文字不阻塞条目。
- 路由 `chronicle-page`；Windows MAUI 侧栏新增“游戏历程”一级入口；Android 未改
  底部 5 Tab，概览页新增“游戏历程（Chronicle）”按钮推入页面（最小侵入，四目的地
  信息架构留待统一导航收口）。
- 不新增 Markdown 导出按钮（writer 已就绪，避免与 UI-3 导出/SAF 语义重复）。

### 验证

| 检查 | 结果 |
|---|---|
| 新增测试 | 54 项：键映射全覆盖、时间语义/精度/偏移、分页与游标失效状态机、空态、排序比较器、相对时间范围、本地化 Markdown |
| Release 全量回归 | 分支上 851/851；rebase 到 UI-4 WP-A1 合并后的 master 后 894/894 |
| 构建 | MAUI Android x64 Release 0 警告 0 错误；MAUI Windows 目标 0 警告 0 错误 |
| 版权头 | Pass |

### 已知限制与后续

- Android 交互验收尚未执行（真机推迟，Emulator 验收随 Android 收口批次进行）；
  `uia-handoff.md` 提供 15 个场景与 AutomationId，含游标失效触发方式。
- 独立 WinUI Chronicle 页待 UI-4 组合根就绪后复用同一展示层与状态机；UC-5 的
  WinUI 角色与武器/周期挑战页、MAUI 四目的地导航与“数据”中心页为后续子阶段。
- 观察/采集时间不声明业务精度，展示为秒级并明确“不适用”；“今年”按页面本地时区计算。

## UI-5-2：独立 WinUI 三个领域页

提交：`2a146cb`（页面与状态）、`d536f5d`（接线与导航）。

- **平台无关状态**（`Presentation/History`）：`HistoryPageSupport`（观察视图/标签/选项构建，
  复用 `CharacterObservationCard`/`ChallengeObservationCard`/`ChronicleTimeDisplay`）、
  `CharacterWeaponHistoryState`、`ChallengeHistoryState`、`ChronicleWorkspaceState`
  （复用 `ChronicleQueryController`/`ChroniclePageState`）。
- **ChroniclePage**：档案/账号局部选择、时间线/粒度/时间范围/领域/星级/状态变化/未注明时间
  筛选、键集分页+加载更多、GenerationId 混代拒绝、游标失效清空+显式重载、游标过期保留+
  提示、四种空态、错误脱敏、详情 + 本地化 Markdown 只读/复制。
- **CharacterWeaponPage**：正式 `IArchiveDomainHistoryStore` 查询；角色/武器列表、最新观察
  默认 + 历史切换（服务倒序）、来源/观察/采集时间/完整度/证据标签、缺失等级/命座保持未知、
  空态、切换取消旧查询。
- **ChallengePage**：三模式（深境螺旋/剧诗/危战）、期次选择、阶段/难度/奖章/最优秒数、
  逐幕阵容/增益/逐怪、最新+历史观察、空态、缺失字段未知、模式/期次切换取消旧查询。
- **接线**：组合根注册正式 `SqliteChronicleQueryService` 与三个页面状态（惰性解析）；
  `MainWindow` 实例化三页、增加导航分支、切页调用 `Leave()` 取消在途查询；启用
  “游戏历程/数据·角色与武器/数据·周期挑战”原占位并移除待交付文案。
- **数据态**：隔离种子只有抽卡事实，角色/武器与挑战页在标准种子上为空态（不伪造领域种子）；
  数据态由 24 项状态测试 + `Ui5WiringIntegrationTests`（正式查询服务 + 隔离库）覆盖。

### 验证

| 检查 | 结果 |
|---|---|
| 新增状态/页面测试 | 24 项（观察视图、角色/武器历史、挑战模式/期次、Chronicle 分页/失效/取消、空态） |
| 接线集成测试 | `Ui5WiringIntegrationTests`：Chronicle 产生非空条目、领域页空态无错误 |
| Release 全量回归 | 938/938（分支基线），接线后 **950/950**（master） |
| 构建 | WinUI Release 0 警告 0 错误（首次全量编译出现既有 2 个 Infrastructure CS8602，重编译归零） |
| Lead UIA | 11 项 Pass：概览覆盖文案已交付、三个导航启用、Chronicle/角色武器/挑战页可达与截图 |

### 已知限制

- UIA 数据态：角色/武器与挑战页在隔离种子上为空态；如需 UIA 数据态需增加隔离合成观察种子
  （当前未做，避免伪造领域事实）。
- 挑战期次列表来自观察聚合，数据量极大时需优化。
- 页面只读，Markdown 仅显示/复制，不写文件、不重新导入；无元数据时回退稳定 ID。
