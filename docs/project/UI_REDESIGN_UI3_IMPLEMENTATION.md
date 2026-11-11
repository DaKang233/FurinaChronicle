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
