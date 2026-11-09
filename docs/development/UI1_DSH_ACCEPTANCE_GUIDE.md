# UI-1 DSH / Computer Use 验收指南

日期：2026-11-02（Asia/Shanghai）

DSH 首轮报告为 `artifacts/ui1/acceptance-20261102-2359/REPORT.md`，双端 U01–U09
通过，子项限制见报告。2026-11-08，修复后的旧 MAUI 隔离验证宿主通过 U10 补测，
覆盖六个入口、原生系统选择器、Merge 预览与取消。补测产物和证据见
[验收修复记录](../project/UI_REDESIGN_UI1_ACCEPTANCE_FIXES.md)。下文指纹对应首轮验收包。

适用代码：`feat/phase-8 @ 5e2a4a5` 加本轮未提交 UI-1 修改。版本源 `0.8.15 / 8C.6`，Schema 11。这里只验收 UI-1，不宣布 UI-2～UI-6、Phase 8C 或生产迁移完成。

## 1. 给 DSH 的任务

使用 Windows UIA 和 Android 原生控件树／ADB 执行下表 U01～U10。先核对产物 SHA-256、完整包名及隔离路径。只使用合成库，不打开主应用真实档案或凭据。操作使用界面控件；只读命令用于设备信息、产物校验与证据。不要修改源代码、SQLite 或断言来制造通过结果。每项记录 Pass / Fail / Blocked / Not run，并附截图或控件树及实际操作步骤。

默认验收 Windows 独立 WinUI 与 Android 独立 UI-1 包。Android 不覆盖安装到主包，也不卸载／清空主包。原 MAUI 关键业务回归另用无真实账号的测试环境执行；不能把生产启动流程称为 UI-1 独立包的隔离机制。

可在具备 Computer Use 能力的 DSH 会话中执行本指南。Windows 系统选择器若未在 UIA 树暴露文件名和打开按钮，可操作已核对归属的对话框原生控件，并用应用返回的文件校验状态确认选取成功。

交付前由 Codex 执行的有限 Windows UIA 脚本已通过分页、范围切换和空状态检查，报告见
下节；它与 DSH 的可见交互、截图和 Android 设备验收分别记录。全量自动化 631/631
通过，WinUI、普通 Android、独立 UI-1 Android 和旧 MAUI Windows Release 构建通过。

## 2. 固定产物

仓库根目录：`E:\Users\14160\source\repos\FurinaChronicle`。

- Windows：`artifacts/ui1/windows/FurinaChronicle.Windows.exe`；必须保留整个相邻目录，不能只拷贝 EXE。自包含、无包身份，不读取生产凭据。
- Android ARM64：`artifacts/ui1/android-arm64/com.companyname.furinachronicle.preview.ui1-Signed.apk`。
- Android package：`com.companyname.furinachronicle.preview.ui1`，应用名 `FurinaChronicle UI-1 Preview`，versionName `0.8.15`、versionCode `215`。仅适用于 ARM64 设备；x64 模拟器需单独按实际 RID 构建，不能用 ARM64 包的指纹代表它。
- 当前测试结果：`artifacts/ui1-tests/ui1-regression.trx`；Windows UIA 脚本报告：`artifacts/ui1/windows-uia-smoke.json`。
- 本轮固定产物清单与 SHA-256：`artifacts/ui1/artifact-manifest.json`。二进制与测试结果被 Git 忽略；后续构建后以该清单和实际文件重新核验，不套用旧指纹。

Android 当前 APK：21,589,226 bytes；SHA-256：
`8BBA4E0AE8CCE3828FC2F13680A736DD440D6D30B19456C8675BB9B38E93031E`。

本轮产物身份为 `0.8.15.215+phase.8C.6.sha.5e2a4a5.dirty`；Dirty 是本轮源码未提交及原有未跟踪图片的实际状态，不是已发布提交。

## 3. 启动与定位

Windows 运行上述 EXE，默认页为“抽卡只读预览”。左侧还保留“组件样例”“主题与材质”“UI-0 边界”，用于离开／返回以及主题验证。

Windows 数据库固定为 `%LOCALAPPDATA%\FurinaChronicle\UI1\WinUI\ui1-preview.db3`，页面底部显示实际完整路径。同目录还有 `metadata`、`images`、`temp` 与会话排他文件 `session.lock`。第二实例不能取得同库租约时，应显示载入失败，关闭第二实例后第一实例仍可重载。不要删除正在使用的库或锁文件。

若执行环境限制访问默认目录，在启动**前**显式设置 `FURINA_UI1_DIRECTORY` 为可写绝对目录。
它仅替换预览的基础目录，数据库仍固定在 `<该目录>/UI1/WinUI/ui1-preview.db3`，
不接受任意数据库文件或生产库路径。自动 UIA 脚本默认用报告旁的 `windows-data`，可用
`-DataDirectory` 显式指定；本轮受限环境的默认 LocalAppData 访问被拒绝，因此使用此工作区路径完成检查。

```powershell
$env:FURINA_UI1_DIRECTORY = 'E:\Users\14160\source\repos\FurinaChronicle\artifacts\ui1\windows-data'
& 'E:\Users\14160\source\repos\FurinaChronicle\artifacts\ui1\windows\FurinaChronicle.Windows.exe'
```

Android 独立包启动后直接显示同一只读流程，没有登录、主库发现或网络元数据启动门槛。数据库位于该独立 package 私有 AppData 下的 `UI1/Maui/ui1-preview.db3`，缓存同目录。Release 的 `run-as` 被拒绝时记录能力限制，不 root、不清数据、不改用主包。

安装与启动（先用真实 serial 替换变量，不将设备序列号写进仓库报告）：

```powershell
$adb = 'E:\AndroidSDK\platform-tools\adb.exe'
& $adb devices -l
$serial = '<选择的设备 serial>'
$apk = 'E:\Users\14160\source\repos\FurinaChronicle\artifacts\ui1\android-arm64\com.companyname.furinachronicle.preview.ui1-Signed.apk'
$package = 'com.companyname.furinachronicle.preview.ui1'
& $adb -s $serial shell getprop ro.product.cpu.abi
& $adb -s $serial install -r $apk
$activity = (& $adb -s $serial shell cmd package resolve-activity --brief $package | Select-Object -Last 1).Trim()
& $adb -s $serial shell am start -W -n $activity
```

界面控件 ID：两端都使用 `Ui1Archive`、`Ui1Account`、`Ui1Summary`、`Ui1Status`、`Ui1Records`。Windows 导航入口为 `Ui1Navigation`。按钮按名称“重新载入”“取消”“上一页”“下一页”定位。Android 顶部“页面往返”打开受控说明页，可用“返回抽卡预览”或系统返回键回来。正常 MAUI 首页入口名称为“打开 UI-1 抽卡只读预览”，ID `OpenUi1Preview`。

每次导航、弹出下拉列表、选择、分页或旋转后重新获取控件，不保存旧元素句柄。Windows 下拉选项可能位于单独 Popup，按应用进程与完整虚构标签匹配。只有视频区域的手机投屏窗口不提供手机控件语义；改用 Android 控件树和 ADB 输入，截图记录坐标所依据的当前布局，不能盲点旧坐标。

## 4. 验收矩阵

| ID | 操作 | 预期 |
|---|---|---|
| U01 初始化／离线 | 双端启动；断网后重载；冷启动再查 | 默认 01 / A 显示 61 条、第 1/3 页；名称含“合成物品”；没有登录或联网门槛 |
| U02 分页与滚动 | 到第 2、3 页，再返回；列表内滚动 | 25 / 25 / 11 条，无重复、无全量堆积；末页“下一页”禁用，首页“上一页”禁用；表头不随列表整页滚动 |
| U03 范围隔离 | 01 / B → 02 / A → 01 | 分别 7 条、3 条，回到 01 恢复 B；同 UID 的 A 在两档案中为不同副本；不出现另一范围的旧行 |
| U04 空状态 | 01 / C；03 空档案 | 分别“尚未记录此账号的抽卡数据”和“此档案还没有游戏账号”；旧记录清空，无分页可用 |
| U05 快速切换／取消 | A→B 连续切换；载入期间取消；再重载 | 最终范围与内容一致；取消后没有迟到记录回填，重载可恢复。过快而无法观察取消时写 Not run，并引用自动化竞争测试，不能臆造人工通过 |
| U06 离开／返回 | 选 B；WinUI 切到组件再返回；Android“页面往返”后返回，含系统返回 | 保留 B 的稳定 ID，重新读取 7 条；离开期间旧结果不更新失效视图；重启应用允许恢复默认 A，本包只保留页面内选择 |
| U07 缺图／来源／时间 | 默认无图片缓存；检查多条记录 | 星级占位、名称和文字仍显示；交替出现“来源未知／用户输入”；发生时间保留固定 +08:00，不替换成导入或当前时间 |
| U08 基本布局／输入 | Windows 1100×720、800×600 DIP，键盘 Tab／方向键／F5；Android 竖屏／横屏及字体 100%／130% | 主要操作与列表可达，焦点可见，主按钮触控范围至少 48dp。窗口物理像素换算为 DIP 时记录实际缩放；完整 UI-2 字体矩阵另跟踪 |
| U09 重开／排他 | 重开独立预览；Windows 同时再启动第二实例 | 不重复新增数据；第一实例保持可用，第二实例不得同时初始化同库；无法取租约显示失败，不接触生产库 |
| U10 原 MAUI 回归 | 在无真实数据的旧宿主环境进入原抽卡／档案／备份页，并执行已有关键文件流程 | 原入口与功能继续可用；管理页选择不被 UI-1 更改。本轮只编译不能算此项通过；没有安全环境则 Blocked |

无档案、管理页改名／删除后回退、取消及迟到结果另有确定性自动化覆盖。UI-1 页面不暴露管理写操作；若没有针对同一隔离库的安全管理夹具，DSH 将这些附加交互写 Not run，不直接用 SQL 修改测试库，不借生产账号验证。

自动 UIA 脚本可作为 U01～U04 的有限辅助（它不保存截图，不验证键盘或视觉）：

```powershell
powershell.exe -NoProfile -STA -File .\eng\ui1\Test-WindowsPreview.ps1 `
  -Executable .\artifacts\ui1\windows\FurinaChronicle.Windows.exe `
  -ReportPath .\artifacts\ui1\windows-uia-smoke.json
```

## 5. 重建与证据

Windows 单目标项目：

```powershell
dotnet publish .\FurinaChronicle.Windows\FurinaChronicle.Windows.csproj `
  -c Release -r win-x64 --self-contained true -m:1 -o .\artifacts\ui1\windows
```

Android 按相同属性定向清理、构建，再复制固定 APK；本机显式指定 SDK，不把路径写进工程：

```powershell
dotnet clean .\FurinaChronicle.App\FurinaChronicle.App.csproj `
  -f net10.0-android -c Release -r android-arm64 -m:1 `
  -p:Ui1PreviewMode=true -p:AndroidSdkDirectory=E:\AndroidSDK
dotnet build .\FurinaChronicle.App\FurinaChronicle.App.csproj `
  -f net10.0-android -c Release -r android-arm64 -m:1 `
  -p:Ui1PreviewMode=true -p:AndroidSdkDirectory=E:\AndroidSDK -p:EmbedAssembliesIntoApk=true
```

当前机器在受限执行环境中曾有 NuGet 访问被拒绝及 MSBuild 任务宿主启动失败；应先排除执行权限，不关闭裁剪、放宽测试或修改 SDK 路径以制造通过。切换目标后若资产文件缺少 `net10.0`，先恢复共享项目的原始目标依赖图，再做对应宿主构建，不把 Android／Windows 的 TargetFrameworks 覆盖为共享项目的目标。

记录目录建议为 `artifacts/ui1/acceptance-<运行ID>/`。每次记录设备匿名别名、系统版本、ABI、字体、缩放、窗口大小、产物指纹、截图／控件树路径、实际操作、结果和限制。不要记录真实 UID、凭据或其他应用内容。脚本启动与隐藏窗口状态不算 DSH 视觉验收，构建成功也不算 Android 设备通过。
