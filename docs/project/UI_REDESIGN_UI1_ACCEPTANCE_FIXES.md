# UI-1：DSH 验收核对与问题修复

日期：2026-11-03（Asia/Shanghai）

补测核对：2026-11-08（Asia/Shanghai）

基线：`feat/phase-8 @ 5e2a4a5` 加未提交修改。

## 验收确认

已核对 `artifacts/ui1/acceptance-20261102-2359/REPORT.md`、双端逐项结果与 U10
构建／启动证据。原文件保留不改写；原审计脚本在新输出位置复跑，37/37 通过，结果
为 `artifacts/ui1/fixes-20261103/DSH-AUDIT-recheck.json`。

- 双端 U01–U09 均为 Pass，包含 Android 真机飞行模式、冷启动取消、横屏和字体 130%。
- Windows 断网子项、人工捕获载入取消窗口仍是 Not run，保留原报告限制。
- U10 原为 Blocked：干净 HEAD 同样启动失败，不能归因于 UI-1，但仍须修复该具体阻塞。
  2026-11-08，DSH 在修复后的 8C.6 隔离验证宿主补测通过，证据见第 3 节。
- Android 横屏列表视口较小、账号字段截断跟踪到 UI-2；不将其改记为本轮失败，
  也不据此宣称完整字体／辅助功能矩阵已通过。

## 1. 旧 MAUI 验证宿主启动崩溃

对 DSH 保留、身份已核验的验证程序使用临时 .NET 启动钩子采集异常类型和调用栈，
确认 `0xC000027B` 的根因链：

1. `UnpackagedPreferencesImplementation` 静态初始化调用
   `FileSystemImplementation.AppDataDirectory`。
2. 默认验证目录 `%LOCALAPPDATA%/User Name/com.companyname.furinachronicle.validation.phase8c6/Data`
   创建被拒绝，抛出 `UnauthorizedAccessException (0x80070005)`。
3. 异常包裹为 `TypeInitializationException (0x80131534)`，最终在 WinUI 启动阶段退出。
4. 原异常日志目录也被拒绝；没有生成日志不能证明没有托管异常。

新证据补充并纠正了原报告的排查假设，不支持因此修复 DDLM、安装其他 Runtime 或降低
系统保护。根因证据为本轮目录中的 `startup-path-trace.log`。

修复限于 Windows 验证组合根：

- `PlatformStoragePaths` 在 8B／8C Windows 验证模式下要求显式绝对验证目录；数据库
  保持原验证文件名，元数据／预览数据进入 `app-data/`，缓存／临时文件进入 `cache/`。
- 验证 `IPreferences` 使用实例内存状态，避免初始化系统默认 Preferences。权威可移植
  设置仍在 SQLite；没有改变设置格式或恢复语义。
- 验证凭据存储禁用：读取为空，写入拒绝，不回退到真实 SecureStorage。
  此宿主不用于通行证登录或凭据迁移验收。
- 普通生产构建仍使用原 MAUI FileSystem／Preferences／SecureStorage；Android 保持
  原包私有存储。Schema、正式应用标识和公开格式均未改变。

修复后已真实启动，UIA 进入主页、抽卡记录、档案修改历史、档案与账号管理、用户、
备份与恢复六个入口。用户页明确为空通行证，隔离业务库档案／账号／抽卡记录读数为
0/0/0。旧生产 Windows DLL 仍为原 SHA-256：
`4641D885667B42BE7148CD816D5207ED73DEF1DA6FF1108CC3A3232F75CAA017`。

11 月 3 日的 UIA 脚本在“选择 .fca 文件”后未能取得系统文件名编辑控件，止于该自动化
前置条件。入口证据为 `fixed-startup-tree.json` 和六页控件树，脚本原始结果保留在
`U10-retest.json`。11 月 8 日，DSH 操作系统对话框的原生文件名和打开控件完成选取，
应用返回文件已校验，后续预览与取消通过。

## 2. 增量切换构建模式的应用标识残留

已核对当前 MAUI 10 MSBuild 目标：Windows 清单的增量输入没有包含应用标识／标题
属性，旧清单可沿增量链传入后续 AssemblyInfo。仅切换验证属性不能保证身份刷新。

新增 `build/FurinaMauiIdentity.targets`：记录应用标识、GUID、标题与版本输入；变化时
只使对应生成清单及生成戳失效，不递归清理目录。生成后校验实际标识／标题，不匹配
则构建失败，阻止错误身份产物进入验收。`Version.props` 仍为唯一版本源。

`eng/ui1/Test-MauiBuildIdentity.ps1` 复用同一 obj、刻意不 clean，连续执行
“生产→8C 验证→UI-1 预览→生产”。四次 AssemblyInfo 与 DLL 身份均正确；不启动
任何产物。日志和结果在本轮 `identity-switch/` 目录。

## 3. 验证与剩余项

本轮证据根：`artifacts/ui1/fixes-20261103/`。

| 项目 | 结果 |
|---|---|
| DSH 原证据审计复跑 | 37/37 通过；原报告未修改 |
| 完整 Release 回归 | 631/631 通过，0 失败、0 跳过；`tests/ui1-fix-regression.trx` |
| Windows 增量身份切换 | 四轮通过，无 clean；生产产物未启动 |
| 8C Windows 验证构建 | 0 警告、0 错误；启动及六个原入口通过 |
| 8B Windows 验证编译分支 | 0 警告、0 错误；未执行运行态验收 |
| Android ARM64 UI-1 修复版构建 | 0 警告、0 错误；独立 `android-build` 目录 |
| 修复版 Android 真机重跑 | 未运行；原 DSH 包的设备证据不冒充新包实测 |
| U10 系统选择→Merge 预览→取消 | Pass，DSH 于 2026-11-08 在修复版 8C.6 隔离验证宿主补测 |
| UI-2 后续 | 横屏密度、完整账号标识展示及更大字体矩阵 |

U10 补测证据位于 `artifacts/ui1/fixes-20261103/u10-dsh-retest/`。`REPORT.md` 记录
操作过程，`SUMMARY.json` 记录判定，`trees/` 和 `screenshots/` 保存界面证据。已复核
补测宿主 EXE、DLL 与 `fix-manifest.json` 一致，合成 `.fca` 的哈希与大小匹配。
Merge 预览显示新建 4 个档案、4 个账号，新增 8 条当前事实和 2 条修订，冲突为 0。
取消后页面显示“当前预览会话已释放；数据库未改变。”，当前隔离业务库 SHA-256
与补测前快照一致。旧宿主进入 UI-1 可见 61 条合成记录，返回管理页仍为空；管理页
证据覆盖空库场景。补测范围为预览与取消，Apply 和真实数据回归未运行。

11 月 3 日的 `fix-manifest.json` 与 `U10-summary.json` 保留当日结果，当前 U10 判定
见补测 `SUMMARY.json`。新产物采用独立 BaseOutputPath，首轮 WinUI、APK 和验收清单
均保留。复现时启动修复后的验证程序：

```powershell
$env:FURINA_PHASE8C6_DIRECTORY = 'E:\Users\14160\source\repos\FurinaChronicle\artifacts\ui1\fixes-20261103\validation-data'
& 'E:\Users\14160\source\repos\FurinaChronicle\artifacts\ui1\fixes-20261103\maui-build\Release\net10.0-windows10.0.19041.0\win-x64\FurinaChronicle.App.exe'
```

在“备份与恢复”选择原 DSH 合成 `maui-u10/data/input/windows-origin.fca`，选择 Merge，
看到完整预览后点“取消当前会话”，记录截图／控件树及取消后无新增数据。
