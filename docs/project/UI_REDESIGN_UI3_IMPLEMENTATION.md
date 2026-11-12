# UI-3 抽卡数据操作实施记录

## 首批增量

- MAUI 抽卡页将原操作入口收进“数据操作”，按刷新、导入、导出和更多分组。Windows
  继续显示网页缓存刷新，Android 提供登录账号和手动链接两条刷新路径。全量模式
  仍显示在页面上。选项调用原流程，沿用既有文件识别、覆盖确认和事务边界。
- 独立 WinUI 抽卡预览接通 UIGF 4.2、CSV 和 XLSX。保存前明确选择当前账号范围或
  档案全部账号，固定档案和账号副本 ID。导出使用正式查询与写出服务，当前视图
  的筛选和分页只影响查看。
- WinUI 先在预览私有临时目录生成文件，再打开系统保存对话框。取消保存会释放
  临时文件。选择目标后先写同目录临时文件，写完后替换目标。结果显示实际保存的
  文件名、账号数和记录数。此入口使用 UI-2 的隔离合成库。

## 本轮验证

证据位于 `artifacts/ui3-export/`。Release 回归 667/667 通过，独立 WinUI 发布和
MAUI Windows／Android ARM64 隔离版构建通过。

- `windows-export-uia.json` 共 17 项通过。WinUI 实际通过系统保存对话框导出 UIGF
  和 XLSX 各 61 条、档案 CSV 68 条。明细筛到 6 条五星时，导出仍覆盖确认范围的
  完整记录。已解析三种产物并核对记录数。系统取消返回后未新增导出文件，临时目录为空。
- `maui-menu-uia.json` 在 Windows 隔离版走查四个分组和 13 个命令入口并取消。
  本轮 Android 验证覆盖构建，Android 菜单和业务操作真机走查待执行。

## 阶段边界

本次只交付首批增量。独立 WinUI 的三种刷新、导入与预览、Portable、纠正、删除和
撤销后续分批接入。原 MAUI 业务页继续承载这些流程，数据库 Schema 和公开格式沿用
已接受的规范。

## 第二批增量：独立 WinUI 文件导入（2026-11-12）

分支 `feat/ui3-winui-import` 从已提交基线 `b3fac79`（master 本地领先 5 个提交）
另建 worktree `E:\Workspace\FurinaWorkspace\wt\ui3-import`；主工作区的 Phase 9／10
在途改动不进入本分支，本分支也不修改 `Version.props`。开工前在该 worktree 复核
Release 回归 717/717。

### 交付内容

- **导入入口**：独立 WinUI 抽卡页命令区新增“导入”（Ctrl+I）与页级导入模式状态。
  使用与窗口关联的系统 `FileOpenPicker`；读取与校验前先识别 UIGF 版本。
- **格式**：UIGF 4.2 正式读取，兼容 UIGF v2.2／v2.3／v2.4／v3.0／v4.0／v4.1。
  SRGF、未知版本、无效 JSON、缺 info 等返回明确中文原因，数据库不变。
- **目标固定**：二选一并固定档案 ID 与账号副本 ID。“当前所选账号”沿用
  `targetGameAccountId` 语义，文件 UID 与目标账号自然身份不一致时阻止；
  “当前档案全部账号”按 UID 在当前档案内匹配副本，缺失时新建，跨档案同 UID
  不匹配、不写入。目标在流程开始固定，确认页再次展示；改变目标会丢弃旧预览并
  重新规划；提交前再次规划并比较范围／档案／账号副本／UID，任一变化即拒绝提交。
- **统一全量模式**：命令区显示“仅补充”或“全量导入：覆盖同记录”，默认保守；
  流程开始时快照，确认页与提交都只用该快照。文案明确“只覆盖来源明确包含的同
  记录并保留修订；不删除来源缺失的本地记录”。
- **预览**：只展示读取服务与规划服务能确认的信息——文件名、识别格式与源版本、
  目标档案与账号身份（名称／UID／区服）、来源各 UID 的记录数与解析问题数、
  可提交／忽略／跳过数、模式、覆盖说明、撤销说明；不为预览物化记录集合。
- **提交与结果**：写路径仍为 `ImportUigfGachaRecords.ExecuteAsync`（原子变更集、
  修订、Tombstone 与容量规则不变）；进度对话框不提供实际无效的取消按钮，关窗
  等待已开始的提交。结果区分“已保存的事实”（总数、新增、重复、无效、忽略、
  新建账号、覆盖修订）与“当前显示是否更新”，成功后按原范围重查并保留稳定 ID。
- **分层**：目标校验、模式快照、预览与中文文案下沉到
  `FurinaChronicle.Presentation/Gacha/`（不引用 MAUI／WinUI／文件系统类型）；
  `Ui1PreviewEnvironment` 只新增同一隔离库上的导入服务组合，不发现生产库或凭据。
  Windows 视图只负责选择器、对话框与渲染。
- 验收中修正了导入状态的可访问名称：状态文本本身成为 `AutomationProperties.Name`，
  使 LiveSetting 的文本对 Narrator 与 UIA 可见（原先被通用名称覆盖）。

### 本轮验证

证据位于 `artifacts/ui3-import/`（artifacts 已被 Git 忽略）。

| 检查 | 结果 |
|---|---|
| Release 全量回归 | 731/731 通过（基线 717 + 新增 14：12 个 Presentation 单元测试、2 个真实 SQLite 集成测试）；日志 `lead-acceptance/` 与队友日志 |
| 独立 WinUI 构建与发布 | Release win-x64 构建 0 警告 0 错误；self-contained 发布到 `artifacts/ui3-import/lead-acceptance/windows/` |
| 版权头检查 | `Manage-CopyrightHeaders.ps1 -Mode Check` 通过 |
| Windows 真机 UIA（Lead 复跑） | 4 个场景共 51 项通过，见 `lead-acceptance/uia/s1..s4.json` 与 `shots/` |

Windows UIA 场景（每个场景使用全新的 `FURINA_UI1_DIRECTORY`，实际操作系统打开
文件对话框，并在宿主关闭后直接查询隔离 SQLite）：

- s1 档案导入、幂等与全量覆盖：预览显示 UIGF 4.2、目标、模式与可提交 2 条；
  首次新增 2，账号 A 变为 63 条；重复导入新增 0／重复 2；切换全量后覆盖 1 条
  既有记录（记录 100001 的改名与星级、`GachaRevisions` 留痕，总数仍 63）。
- s2 旧版兼容：v3.0 文件识别为“UIGF 3.0（兼容读取）”，导入到当前账号 C 新增 2 条。
- s3 拒绝与取消：系统选择器取消不报错；无效 JSON、SRGF、v4.3 与目标 UID 不匹配
  均给出中文原因，账号 A／B 记录数保持 61／7。
- s4 跨档案隔离：对档案“02 同 UID 独立副本”按档案导入后该副本 3→5 条，档案
  “01 合成档案”中同 UID 的副本仍为 61 条。

### Android 真机复走（MAUI 数据操作）

在独立 worktree `wt/ui3-android` 以 `Phase8CValidationMode=true` 构建隔离验证包
（`com.companyname.furinachronicle.validation.phase8c6`，0.8.16／240，arm64-v8a），
在真机 V2353A（Android 14）实际走查。Android 侧呈现 12 个入口（“通过网页缓存
刷新”仅 Windows）：

- Pass：导入 UIGF 到档案（3 条）与所选账号（2 条）、Portable 导入预览与取消、
  档案／账号 UIGF 与表格导出、Portable 导出、清除图标缓存；另执行了明细页
  “普通删除 → 撤销”闭环（5→4→5），永久删除确认后取消。
- Blocked：通过登录账号刷新、输入手动链接刷新、提瓦特小助手导入（需通行证
  SToken／真实 authkey／第三方请求，未输入任何真实凭据或链接）。
- 发现待处理缺陷（本轮只记录，未改源码）：D1 未登录时“通过登录账号刷新”错误被
  页面重载覆盖、无可见提示；D2 Android 导出结果展示原始 `content://` URI；
  D3 明细表格记录列表纵向可视区约 1 行。证据 `artifacts/ui3-android/REPORT.md`。

### 后续工作包

独立 WinUI 尚未接入：三种刷新（手动链接优先，SToken／网页缓存需先确定隔离写库
与凭据边界）、提瓦特小助手导入、Portable 导入／导出、纠正／普通删除／永久删除、
清除图标缓存；撤销入口属 UI-4。Android 的 D1～D3 与 Phase 10 的交接项见
`artifacts/ui3-handover/PHASE10_RECON.md`（Phase 10 在当前 master 上尚无可用
提交，本分支不引用其类型）。

## 第三批增量：WinUI 数据操作（2026-11-12，0.10.0 基线）

主阶段已完成 Phase 10 并提交（`b943914`：版本 `0.10.0 / 10.0`，Phase 10 Closed，
退出评审记录全量回归 752/752）。UI-3 分支从 `b3fac79` rebase 到该基线，rebase 后
先复核 **766/766**（新基线 752 + 首批 14）通过，确认基线一致后再继续。本批代码
提交 `a360d81`、`b198ef6`。

### 交付内容

- **Furina Portable 抽卡数据集导入**：系统选择 `.zip` → 私有暂存与磁盘索引（不整体
  物化记录）→ 规划预览（目标档案、账号映射与身份、新建账号、新增／覆盖／保留／
  冲突、别名）→ 目标在流程开始时固定，“更改目标”只能重新规划，提交前再次规划并
  拒绝过期预览 → Tombstone 逐次重引入确认、撤销材料清理确认 → 正式
  `IGachaPortableImportApplier` 原子提交（OperationId 幂等）→ 结果与范围刷新。
- **Portable 导出**：范围选择（当前选择／档案全部账号）→ 明确“抽卡数据集，不是
  完整应用备份” → 私有临时文件生成与校验 → 系统保存对话框；取消保存释放临时文件。
- **记录操作**：明细详情（侧栏或窄布局对话框）接入 纠正／普通删除／永久删除；
  计划冻结当前 `FactVersion`，提交前重取并在变化时拒绝；显示影响范围与
  Revision／撤销资格；永久删除必须勾选不可撤销确认后才可提交，并写 Tombstone。
  撤销入口属 UI-4，本批只给出资格说明。
- **清除图标缓存**：数据操作菜单内确认后只清缓存，不删除事实、档案或元数据。
- **手动链接刷新**：密码框输入、URL 不回显不落盘、本地校验（域名／authkey）＋
  目标账号 UID 比对＋全量模式快照；结果与异常文案不含 URL。SToken 刷新与 Windows
  网页缓存刷新**未接线**：隔离宿主没有凭据与生产库路径（ADR 0020 迁移边界），
  菜单不提供入口，登记为后续工作包。
- **交互与边界**：所有数据操作与首批 UIGF 导入／导出共用忙碌互斥；提交阶段进度框
  不提供无效取消按钮；关窗等待已开始的写入；数据操作状态文本作为可访问名称，
  LiveSetting 对 Narrator／UIA 可读（验收中对状态可访问名做了修正）。

### 本轮验证

| 检查 | 结果 |
|---|---|
| Release 全量回归（Lead 复跑） | **797/797** 通过（基线 766 + 新增 31：12 Portable 规划、7 记录操作、9 手动刷新、3 真实 SQLite 集成） |
| 独立 WinUI 构建／发布 | 0 警告 0 错误；干净提交树发布标识 `0.10.0.252+phase.10.0.sha.b198ef6` |
| 版权头检查 | Pass |
| 首批 WinUI UIA 回归（新构建） | `r1` 27 项、`r4` 7 项共 **34 项 Pass**（真实系统文件选择器、预览、幂等、全量覆盖、跨档案隔离） |
| 第二批新菜单路径的脚本化 UIA | **Not run（工具限制）**：MenuFlyout 项位于独立弹出窗口，Windows PowerShell UIA 客户端可读取其 AutomationId 但无法可靠选中；系统打开／保存对话框的文件名字段在该环境未暴露给 UIA。Computer Use 手工确认“数据操作”菜单项存在且带正确 AutomationId，点击“导入 Furina 抽卡数据集…”能打开系统选择器；其余流程由上述 31 项自动化（含真实 SQLite 集成）覆盖 |

第二批 UIA 的证据与限制记录在 `artifacts/ui3-import/lead-acceptance/`（脚本与回归
结果 `uia/r1.json`、`uia/r4.json`）。未把自动化测试结果当成交互验收结果。

### Android Emulator 验收（0.10.0 基线）

在独立 worktree `wt/ui3-android`（HEAD 等于 master）以
`Phase8CValidationMode=true`、`-r android-x64` 构建隔离验证包，在 AVD
`Pixel_7_API_36`（API 36，x86_64，411dp 宽）上走查 Android 呈现的 12 个入口：

- 包身份：`com.companyname.furinachronicle.validation.phase8c6`，versionName
  `0.10.0`、versionCode 247、ABI `x86_64`，APK SHA-256 记录在报告中；未触碰主包。
- 导入 4 项 Pass（UIGF 3 条 + 2 条；提瓦特小助手阻断／提示／取消；Portable 预览
  0 新增／0 覆盖／保留 5 且取消不写入）；导出 5 项最终 Pass（UIGF×2、XLSX×2、
  Portable ZIP，均核对结构与 SHA-256）；清除图标缓存 Pass；明细页“普通删除→撤销”
  闭环 5→4→5 Pass；永久删除只核对入口与确认文案（未执行）。
- 刷新 2 项 Blocked（缺真实通行证／authkey，未输入任何凭据或链接）。
- **新发现 D4（高）**：Android 导出间歇性静默失败——点击导出后约 7/12 次无系统
  保存对话框、无成功／失败提示、无文件落盘，页面被自动重载；日志对应时间窗没有
  `CREATE_DOCUMENT`／DocumentsUI 启动。候选原因：导出选项页关闭触发
  `GachaPage.OnAppearing → InitializeAsync` 置 `IsBusy`，随后的
  `CreateUigfExportAsync`／`CreateTableExportAsync` 经 `ExecuteBusyAsync` 静默
  return，界面直接返回。相关文件在 `b3fac79..b943914` 之间无改动，更像平台／时序
  敏感（Android 16 模拟器），需开发确认是否回归并做确定性复现。
- D1 部分复现（进度页短暂显示失败原因，页面重载后仍无持久提示）；D2 复现
  （完成提示与状态行暴露 `content://` URI）；D3 未能等同复现（411dp 下同屏 4 行，
  横向滚动与“永久删除”被裁切仍成立）→ Not run／部分。
- 其他观察：0.10.0 启动期新增元数据网络依赖，冷启动无网络时停在“初始化失败”；
  导出 UIGF 的 `export_app_version` 固定为 `1.0.0`，与 0.10.0 不一致（非本轮引入）。
- 证据：`wt/ui3-android/artifacts/ui3-android-emulator/REPORT.md`（73 截图、
  74 控件树、5 个导出产物、脱敏日志；模拟器保持运行）。

### 缺陷与后续工作包

| 编号 | 优先级 | 内容 | 建议 |
|---|---|---|---|
| D4 | 高 | Android 导出间歇静默失败（无提示） | 先做确定性复现并修 `ExecuteBusyAsync` 在模态页关闭竞态下的静默 return，补真实设备／模拟器回归 |
| D1 | 中 | 未登录刷新失败原因被页面重载清除 | 在进度页／页面状态持久展示错误，避免 `OnAppearing` 覆盖 |
| D2 | 低 | Android 导出完成提示展示 `content://` URI | 展示用户可理解的文档名／定位方式 |
| D3 | 中 | 明细表格纵向可视区与横向滚动 | 在 360dp 真机与 411dp 模拟器复测后调整 `DetailView` 高度分配 |

独立 WinUI 仍未接线：SToken 刷新、Windows 网页缓存刷新（需先确定隔离写库与凭据
边界）、提瓦特小助手导入、操作历史撤销（UI-4）、批量记录操作、Portable 撤销材料
清理的真实容量路径。Android 侧 D1～D4 需单独授权修复并补真机／模拟器证据。
