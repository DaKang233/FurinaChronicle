# FurinaChronicle 8C.6：修复后 DSH / UIA 补测指南

日期：2026-10-29（Asia/Shanghai）
修复基线：包含 `8e36948` 的 `feat/phase-8`；软件里程碑 `0.8.15 / 8C.6`，数据库 Schema 11。
用途：供 DSH Computer Use / UIA Agent 执行修复回归和剩余设备验收。本文不是设备通过报告。

## 1. 执行任务

> 先核验本轮固定产物、包名和测试库，再按 R01～R08 执行。优先验证原始种子第一次导入后再导出能够严格 Equivalent；两端均要从全新目标开始。保留上一轮所有原始文件和报告。UI 操作使用正式“备份与恢复”页面及系统选择器；只读命令用于校验和、语义比较、指标和 Receipt 诊断。未测项明确写 Blocked / Not run，不能因为功能基本正常就关闭阶段。不要修改源代码、原始种子、比较器忽略规则或主应用数据来使验收通过。

完整控件定位、弹窗名称、通用 Merge／导出／重开步骤沿用
`E:\Workspace\FurinaWorkspace\FurinaChronicle_Phase8C6_DSH_UIA_Acceptance_Guide_20261028.md`。
本文件覆盖其中已经变化的 Schema、修复要求和剩余项目。

## 2. 本轮修复与旧证据的解释

1. `GameAccount.UnresolvedReason` 现在保存来源说明，SQLite Schema 11 为其增加列。
   自定义字符串和显式 null 都保留；不要替换为 `placeholder_account`。
2. Schema 10 及更早版本升级时，只沿用旧 exporter 能证明的本地诊断：
   解析账号为 null，占位账号为 `placeholder_account`，其他未解析账号为
   `legacy_unresolved_identity`。旧版已经丢失的自定义原因无法自动恢复。
3. 账号身份补全后清除不再适用的未解析说明。它不代替现有身份诊断服务。
4. 验证工具现在容忍采样期间文件或目录被正常删除；权限错误仍报告失败。
5. 严格比较规则没有放宽。旧 seed→旧设备起点的两组 Different 仍应是 Different；
   这是有效负例，不能把旧文件重命名后报告修复通过。

工程验证：616 项 Release 测试通过，包括两份验收工具实际种子的双数据库往返、
重复 NoOp、自定义／空原因、v10→v11 迁移与失败回滚，以及确定性采样竞态测试。
旧证据的 18 组比较重新核对为 15 Equivalent / 3 Different；额外 20 次采样回归正常。
这些属于工程证据，本轮仍需实际设备验证。

## 3. 产物、环境与隔离

- 仓库：`E:\Users\14160\source\repos\FurinaChronicle`。
- 原始种子：`E:\Workspace\FurinaWorkspace\phase8c6-artifacts\windows-origin.fca`、
  `android-origin.fca`。
- 上轮证据：`phase8c6-artifacts\run-20261028-0115`；只读保留。
- 新证据目录：`phase8c6-artifacts\run-20261029-<本次运行ID>`。
- 新 Android 回归包名：`com.companyname.furinachronicle.validation.phase8c6.retest`。
  该包用于一个方向的全新库。另一个方向使用另一个全新沙箱，例如 `.retest.a2w`；
  不复用已装入 Windows 种子的库导入 Android 种子。
- Windows 各方向仍用不同的 `FURINA_PHASE8C6_DIRECTORY` 绝对目录；只在进程创建前设置。
- 主包 `com.companyname.furinachronicle.app` 不在测试范围。

本轮已准备的固定产物（均对应 `8e36948 / 0.8.15.214`，尚未设备启动验收）：

- Windows EXE：`FurinaChronicle.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\FurinaChronicle.App.exe`。
- Android APK：`FurinaChronicle.App\bin\Release\net10.0-android\android-arm64\com.companyname.furinachronicle.validation.phase8c6.retest-Signed.apk`。
- Android 包大小 21,907,421 bytes；SHA-256：
  `145db5f14e2cc1b86b46ae6a1a11a8bee4299e37b485c4c2e6c3de6c478fc8e6`。
- 上述路径均相对本节的仓库实际路径。主机文件可能在后续构建时更新，运行前必须重新
  校验；若已不同，按真实新产物记录，不能继续沿用该指纹。

准备时记录实际完整版本、Commit、Dirty、APK ABI／签名／SHA-256、Windows EXE／程序集
身份；Build 随 Git HEAD 改变，不把旧 `.213` 当作修复版本。若本轮产物按修复提交
`8e36948` 构建，其数字 Build 为 214；之后的文档提交不代表该 APK 已重新构建。

Windows 必须使用无真实通行证的测试用户环境，或已有证据证明系统凭据空间隔离。
数据库目录隔离不等于 Preferences／SecureStorage 隔离；当前修复没有更换这些服务。
若侧栏出现真实通行证，记录环境不符合要求，不清空、读取或导出凭据来补救。
首次启动需要的公共元数据准备也单独记录。

2026-11-03 补充：以上“没有更换这些服务”为 2026-10-29 的历史状态。当前 Windows
验证构建改为强制要求绝对 `FURINA_PHASE8C6_DIRECTORY`，全部 AppData／Cache 使用该
根下的 `app-data`／`cache`；Preferences 仅保留会话状态，凭据存储禁用，不回落到生产
存储。Android 继续使用独立包私有目录。本地权威可移植设置仍在验证 SQLite 中。
这不表示登录迁移已验收。普通生产构建的目录与系统存储机制不变。

切换三种验证／预览模式时，当前 `FurinaMauiIdentity.targets` 会按应用标识、名称及版本
输入使生成 Windows 清单失效，并校验实际生成的标识和标题。旧版本仍须定向清理相同
TFM／配置／RID 的中间目录；新版本也必须在启动前核对 AssemblyInfo 与 DLL 的
`AppInfo.PackageName`。验收输出用独立 `BaseOutputPath`，不得覆盖现有生产产物。
增量回归脚本见 `eng/ui1/Test-MauiBuildIdentity.ps1`。

Android 的 UIA 能力沿用上一轮已验证方案：手机原生控件树／截图，加真机镜像或 ADB
输入。Windows UIA 只看到一个投屏视频区域时，不要宣称它能读取手机控件。
安装前重新核对设备别名、ABI、签名和完整包名；不用旧 transport ID 盲选设备。

## 4. 结果记录方式

为每个用例保存：

- 固定产物与隔离实例；
- 操作前后截图或 UIA 树；
- 当前 Preview、映射和确认内容；
- Receipt、实际输入／输出文件、SHA-256、比较报告及退出码；
- 时间与内存／空间采样方法；
- `Pass / Fail / Blocked / Not run`。

原始目录不改写。新报告可以引用旧证据中仍成立的功能，但修复后的行为必须使用新版本
实际执行。不能把新工具读取旧导出文件的结果当成新客户端的设备验收。

## 5. R01：Windows 首次导入与新原因保真

1. 启动修复后的 Windows 隔离构建，使用从未使用过的新测试目录。
2. 在“备份与恢复”点“选择 .fca 文件”，通过系统选择器打开原始
   `windows-origin.fca`。
3. 选择“Merge：补充／合并，不删除目标事实”；开启“应用来源可移植设置”，等待预览。
4. 预期：4 个新档案、4 个新账号，当前事实 8 条及来源修订；无冲突、无意外删除。
   不把 UIGF 标准通道和 history 补充的计数重复相加。
5. 核对确认项后提交，保存 Receipt；关闭进程并用同一目录重开。
6. “导出全部档案”开启，通过正式保存选择器导出 `W0-fixed.fca`。
7. 严格比较 **原始种子→W0-fixed**，必须 `Equivalent / exit 0`，不能只比较之后的两跳。
8. 检查导出 catalog 的未解析账号：`unresolved_reason` 必须仍为
   `phase8c6_synthetic_unresolved`。原始偏移、未知时间、空父对象和设置也应被严格比较覆盖。
9. 重复导入同一原始种子；显式映射 Unresolved 到已存在的未解析账号，不能创建新副本。
   预期 NoOp；再次导出仍与原始种子 Equivalent。

失败条件：原因重新变成 `placeholder_account`、原因被丢弃、空父对象消失、
重复导入形成新账号，或者必须忽略更多字段才能通过。

## 6. R02：Android 首次导入与双向返回

### Windows 发起

1. 将 R01 的 `W0-fixed.fca` 转移到新 Android `.retest` 的本地文件目录，核对传输哈希。
2. 在手机“档案”→“备份与恢复”区域“打开”，用系统选择器选择文件。
3. 以 Merge + 来源设置开启导入；记录 Preview／Receipt，终止并冷启动同一测试包。
4. 重复导入，显式映射 Unresolved，预期 NoOp。
5. 通过正式系统保存选择器导出 `W1-fixed-android.fca`，复制回主机。
6. 原始 Windows 种子→W1-fixed，以及 W0-fixed→W1-fixed，都必须 Equivalent。

### Android 发起

1. 使用第二个全新 Android 沙箱，正式导入原始 `android-origin.fca`；
   导出 `A0-fixed.fca`。先比较 **原始 Android 种子→A0-fixed**，必须 Equivalent。
2. 在全新 Windows A→W 测试目录中导入 A0-fixed，重开并重复导入，预期 NoOp。
3. Windows 导出 `A1-fixed-windows.fca`，与原始 Android 种子及 A0-fixed 分别比较。
4. 将 A1-fixed 传回 Android A→W 实例，通过系统选择器读回并 Preview。
   既有事实不应被替换／删除；返回包可能有另一设备新增的接收修订。
5. 取消 Preview 后再导出，仍应与 A0-fixed Equivalent。

比较器在 Windows 宿主执行，手机承担实际文件读写／恢复工作；报告按实际平台写明。

## 7. R03：真实 Schema 10 副本升级

本项用于设备级升级补证，不是修复首次导入的替代方案。

1. 由开发者准备已确认 `user_version=10` 的合成库或受控一致副本，保留未修改原件。
   活动 SQLite 不能只复制主文件遗漏 WAL；使用 SQLite 一致备份，或在所有连接关闭后
   由开发者确认完整副本。不要直接升级上轮原始证据库。
2. 先只读记录 Schema、application_id、账号／事实计数、关键标识及来源。
3. 用新客户端打开该副本；确认实际 Schema 11、外键检查无违规、原账号与事实归属不变。
4. 重开、导出、重复初始化均正常。未解析原因只呈现第 2 节规定的可证明旧诊断。
5. 不要求旧库恢复 `phase8c6_synthetic_unresolved`：该字符串已经在旧客户端丢失。
   修复验证必须依靠 R01／R02 全新目标，不能从旧设备起点重新定义基准掩盖差异。

客户端界面没有普遍可用的 Schema 查询按钮；由现有只读诊断或开发者查询受控副本补证，
不要假装从界面读到了版本。自动化已覆盖 v1～v10 适用迁移与失败回滚。

## 8. R04：验证工具采样竞态

使用新编译的验证工具。先确认 DLL 对应修复提交或之后的同代码版本，不能运行上轮旧 DLL。

```powershell
dotnet build .\eng\phase8c\FurinaChronicle.Phase8CValidation -c Release
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -c Release --no-build -- compare <expected.fca> <actual.fca> --report <new-report.md>
```

- 重跑旧报告中的 18 组比较，预期仍为 15 Equivalent / 3 Different。
- 对 `D0-initial-android.fca → D2-after-replace-android.fca` 至少重复 20 次，
  全部正常返回 Equivalent，不允许自动重试后才统计为一次通过。
- 过程中不得再有 `staging.db`／`staging.db-journal` 消失导致的
  FileNotFoundException／DirectoryNotFoundException。
- 每次记录退出码和耗时，报告写新目录。
- 不要主动撤销目录权限来“模拟删除”：权限错误与正常临时文件回收是不同场景。

本项 18+20 次宿主复查已由修复 Agent 完成；设备 Agent 若复用结果，应引用该工程证据，
而不是重复声称自己又执行了一次。

## 9. R05：补 Android Tombstone

使用修复版一次性测试实例。可以在 R02 完成且文件证据已保存后，将该测试库明确转为
一次性删除场景；报告写清复用关系，不能再将其称作全新库。

1. 确保该实例加载 Windows 种子；导出删除前全量文件，以及“同 UID 独立副本”单档文件。
2. 档案管理选择 `Windows 主档案 / Windows 旅行者 (800000001)`，
   点“删除所选账号”；保存“删除游戏账号”的本地不可逆删除确认后执行。
3. 重开，确认主账号已删除，同 UID 的另一档案副本保留。
4. 打开删除前完整 Archive，选 Merge；将已有 Unresolved 显式映射回原副本。
   Preview 必须报告活动 Tombstone，包含来源修订触及的内容。
5. 在重引入确认中取消，再取消当前会话，重开。被删除内容不能因 Preview 恢复。
   导出受保护副本，与删除前该单档包比较 Equivalent。
6. 再次读取同一包，保存 marker/version 绑定的警告，明确接受后提交。
7. 重开后确认当前事实及来源修订恢复；再次导入为 NoOp。
8. 删除／重建账号可能引入预期父属性差异，不能靠忽略所有父属性取得全库 Equivalent。
   按声明的事实／修订和未选副本验证。

不使用真实 SToken 或官方账号。Windows 已有 Tombstone 证据可保留引用，但不能替代本项。

## 10. R06：补 Preview／Apply 资源采样

两端分别记录 Preview、Apply、重开、导出耗时；每阶段至少有基线、过程采样与稳定后值。

- Windows 按测试 PID 采集 WorkingSet64 / PrivateMemorySize64，建议 0.5 秒一次。
  不把启动后单点称为峰值，不混入其他测试进程。
- Android 按完整测试包采集 TOTAL PSS，使用可行的固定间隔并注明采样开销；
  小样本短操作可能漏峰，记录 `sampled peak`，不宣称硬上界。
- Windows 可在已确认的测试用户缓存目录观察 `archive-restore-sessions`、
  `archive-export-snapshots` 和 `archive-product-exports`；实际路径先由开发者核定。
- Android Release 私有缓存通常不可由普通 ADB shell 读取。没有经核验的只读遥测能力时，
  staging 峰值保持 Not measured，不能用 ZIP 展开大小或宿主比较器临时空间代替。
- 不为采样而 root 设备、绕过系统权限或填满磁盘。需要验证模式诊断支持时交给开发者实现，
  该支持目前尚未由本轮修复新增。

## 11. R07：重开后 Receipt 持久性

本轮没有新增“按 Receipt ID 查询历史”页面。使用已有只读诊断补证即可，无需完整 UI 重构。

1. 在新设备流程保存成功弹窗中的 ReceiptId，并记录本次输入包 SHA-256。
2. 关闭并重开同一实例；由开发者使用 `FindReceiptAsync` 或对受控一致副本的只读 SQLite
   查询，核对原 Receipt 仍存在、包指纹／OperationId／模式／映射一致。
3. 若提供只读 SQLite 工具，可参考：
   `SELECT ReceiptId, OperationId, PackageFingerprint, Status, Mode, RecoveryPointId FROM ArchiveRestoreReceipts WHERE ReceiptId = '<本轮ID>';`
   字段来自当前 Schema；数据库以只读方式打开，不调用 InitializeAsync 触发额外升级。
4. 新一次重复导入产生新的 NoOp Receipt，不等于查询到了原 Applied Receipt。
5. Android 私有库无法合法只读访问且无受控诊断时，本项记 Blocked，保留现有自动化证据。

禁止复制主应用数据库。截图只证明弹窗出现，不能代替重开后的原 Receipt 查询。

## 12. R08：事务写入期间进程终止

Preview 后终止已在上一轮双端通过；本项专门验证事务写入窗口。

1. 必须由开发者先提供可观测、只用于受控验收的阶段标识，能证明已经开始写入但尚未提交。
   当前修复未新增该诊断；普通 UIA 的忙碌动画不具备这个证明力。
2. 准备合成库与基线导出；在明确阶段终止指定测试进程。
3. 重开后核对旧事实／归属／设置和 Receipt；事务应整体回滚，不得残留半份跨领域状态。
4. 另一次操作确认已提交并保存 Receipt 后终止；重开应能找到原结果，不重复写入。
5. 无阶段标识时记 `Not run: timing not observable`。不以随机点击后立即 kill、Preview
   终止或故障注入测试替代实际事务时机证据。

## 13. 退出与报告修订

新报告独立保存，并引用旧报告仍成立的证据。修复回归的首要退出条件：

- R01／R02 原始种子→新客户端首次落库导出→对端返回均 Equivalent；
- 原因与显式空值不被规范化丢失；
- R03 控制副本升级不改归属、不补造来源；
- R04 工具正常回收临时文件时不再中断。

剩余设备项目分别记录 R05～R08 的真实结果与能力限制。未完成指标／诊断不能写成 Pass。
上轮统计应在新报告勘误为 18 组中 15 Equivalent / 3 Different；不要覆盖原始报告。
整体状态先保持 `Partial device evidence`，由维护者依据全部退出条件确认是否关闭 8C.6。
