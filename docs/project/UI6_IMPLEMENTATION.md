# UI-6 双端收口实施记录

状态：Delivered（2026-11-13）。目标：完成 UI-6 的双端收口（打包/默认入口/迁移前置/日志/无障碍/性能），
并把 UI-3 ~ UI-6 的全部更改合并 master。生产库的真实数据迁移与“生产组合根启用”仍需在用户真实
数据上做一次演练后单独执行（见 §5），不属于本次 UI 重构的代码交付范围。

## 1. 默认 Windows 入口与打包

- 默认入口：独立 WinUI 3 宿主 `FurinaChronicle.Windows`（unpackaged、self-contained、win-x64）。
- 新增 `eng/Publish-WinUI.ps1`：发布、复制许可/说明、生成 `BUILD-INFO.txt` 与 `SHA256SUMS.txt`，
  可选 zip；版本来自 `Version.props`，要求可靠版本。
- CI：默认 Windows artifact 改为 WinUI 宿主 `FurinaChronicle-windows-win-x64-<commit>`；旧 MAUI
  宿主保留一个发布周期为 `FurinaChronicle-windows-legacy-win-x64-<commit>`。
- 文档：`docs/project/UI6_RELEASE_PACKAGING.md`（安装/升级/回退/迁移预期/验收清单/已知差距）；
  README 增加构建与运行入口；CI 文档同步。
- 不改变应用标识/Publisher/ApplicationId/打包身份，避免数据路径漂移。

## 2. 单实例与单写者（UI-6a）

- 命名 Mutex 按数据目录哈希（Local）在创建任何 SQLite 前获取；同一数据目录第二实例明确提示后
  退出，不产生第二个写者（不同数据目录允许并行，便于验收）。
- `session.lock`（数据根）独占写；记录 PID/启动 UTC/主机/版本/宿主类型；正常退出释放；
  崩溃后的陈旧锁可安全接管并通过启动 InfoBar 提示；活进程占用返回 SessionBusy，不建库。
- 旧 MAUI/其他宿主只读探测（进程名 + initialize.lock），迁移时提示先关闭旧宿主。

## 3. 生产复制迁移向导（UI-6a）

- 首启只读探测：合成夹具通过 `FURINA_UI6_SOURCE_DIRECTORY` 注入；未设置时只读取生产路径目录
  元数据，不读库内容；无显式确认不迁移，默认仍隔离。
- 流程：只读校验源库（application_id/Schema/quick_check）→ 旧宿主检测 → 强制 SQLite 一致性
  备份并校验 → 复制到隔离数据根的 `UI6/Data/furinachronicle.db3` → 副本完整性与哈希校验 →
  原子发布（旧目标 `.previous` 回退）→ 写脱敏 `UI6/migration.json` 标记（Receipt/备份/源信息哈希）。
- 失败安全：任一步失败不切换、源库不修改、不替换已有目标、清理临时文件；错误经 LogSanitizer 脱敏。
- 幂等：Completed + 目标校验通过不重复；显式强制重新迁移需用户确认。
- 回退/凭据：页面明确源库与强制备份保留、回退到旧宿主、旧凭据不解密不迁移需重新授权。
- 边界：本交付只**准备并校验迁移副本与标记**；当前构建仍在隔离宿主启动，生产组合根/默认入口
  运行时切换属于生产发布步骤（§5），页面与文档均如实说明，不伪造已切换。

## 4. 日志、无障碍与性能

- `LogSanitizer`：清理绝对 Windows/UNC 用户路径、完整 URL/查询串、stoken/ltoken/authkey/cookie/
  mid/password/secret 键值；构建失败日志改用脱敏输出；启动时删除旧未脱敏临时日志。
- 无障碍：UIA 枚举 9 页约 210 个交互控件；修复游戏历程 Markdown 报告 TextBox 缺失可访问名称后，
  **全部页面 missingName=0**；新 UI-4/UI-5 页面 AutomationId 0 缺失；键盘遍历稳定循环无焦点丢失。
- 性能：窗口出现约 0.5–0.9 s；概览就绪约 1.5–5.8 s（首次播种/并发构建时更慢，已注明需空闲环境
  复测）；已构建页面切换约 0.02–0.13 s。
- 明细与人工检查项：`docs/project/UI6_ACCESSIBILITY_PERFORMANCE.md`。

## 5. 验证与证据

| 项 | 结果 |
|---|---|
| Release 全量回归（master） | **980/980** 通过（967 + 13：锁/迁移/脱敏） |
| WinUI 构建/发布 | 0 警告 0 错误；`eng/Publish-WinUI.ps1` 产物含 BUILD-INFO/SHA256SUMS |
| Android x64 构建 | 0 警告 0 错误（Android 最终 Emulator 验收见 `android-final-verification/REPORT.md`，12/12 SAF） |
| 迁移端口 self-check | PASS（真实 SQLite 备份/复制/校验/源不变/标记/幂等 + session lock） |
| 版权头 | Pass |
| Lead UIA（迁移成功路径） | 8 项 Pass：迁移页源/目标/回退/凭据文案、结果对话框、目标库+标记+备份存在、源库哈希不变；`Continue`/重启幂等因 UIA 进程关闭在测试机上被 session.lock 阻塞，改由 7 项迁移集成测试覆盖（含幂等/重启不重复） |

自动化证据：`wt/ui4-winui/artifacts/ui6-winui/{REPORT.md,uia-handoff.md,selfcheck}`、
`lead-acceptance/uia/ui6a*.json`；发布产物 `artifacts/ui4-winui/windows`。

## 6. 剩余生产发布步骤（需真实数据演练，非本次 UI 代码交付）

1. 在真实用户数据上演练迁移向导：强制备份 → 复制 → 校验 → 回退；
2. 启用生产组合根（P2）：迁移标记 Completed 后以迁移副本启动、关闭合成播种，验证全部页面在真实
   规模数据上的查询/写入/导出/撤销；
3. 切换默认分发入口（CI 已是 WinUI 默认），保留旧 MAUI 宿主一个发布周期；
4. 真实凭据需用户重新授权；SToken 成功网络路径需授权环境验证；
5. 旧宿主移除与生产组合根上线另行评审/确认。

在这些步骤完成前，普通构建仍只读隔离数据目录，不会自动迁移或改写生产数据。

## 7. 后续修订（2026-11-13，用户指示）

- 生产数据改为**同一数据库就地使用**：WinUI 直接打开 MAUI 宿主的生产 DB，不再复制到 `UI6/Data`；
  首启只读校验 + 强制备份 + 脱敏接管标记，失败不改原库；`FURINA_UI1_DIRECTORY` 仅用于隔离/测试。
- CI 移除 MAUI Windows x64 编译，Windows 只构建 WinUI；旧 MAUI Windows 宿主长期保留为兼容性/
  基本功能查看与验证用途（无专门 UI 优化），不进入 CI 产物。
- 迁移向导相应改为“接管现有数据（同一数据库）”；单实例/旧宿主检测防止同时运行。
- 上述改动实现后，本记录 §5/§6 的“复制迁移/生产组合根待启用”描述被本节取代；最终验证数字在
  实现完成后的提交/报告中更新。

### 最终实现与验证（2026-11-13）

- 生产模式：WinUI 默认直接打开 MAUI 同一 `furinachronicle.db3`；显式 `FURINA_UI1_DIRECTORY` 仍为隔离合成模式。
- 首启接管：只读校验 → 强制备份 → **临时副本完整 Schema 校验**（不完整库在写标记前拒绝）→ 脱敏标记 → 同一库进入应用；已接管幂等；失败不改原库。
- CI：Windows job 仅构建 WinUI 3（移除 MAUI Windows x64 编译），Android job 保持 MAUI；旧 MAUI Windows 宿主保留源码用于兼容性/验证。
- 验证：**980/980** Release tests；WinUI/Android 构建 0 警告 0 错误；版权头与 `git diff --check` 通过；Lead UIA 接管验收 **17 项 Pass**（初始化/接管+备份+标记/同进程进入/幂等/无效库/不完整 Schema/隔离模式），隔离回归 u1 30、u2 4、UI-5 11、登录 12 项 Pass；Windows self-check 覆盖不完整 Schema 拒绝与完整库接管。
