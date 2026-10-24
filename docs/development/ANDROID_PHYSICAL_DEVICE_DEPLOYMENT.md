# Android 真机构建与部署

状态：Accepted

本文记录 FurinaChronicle 在 Windows 开发机上生成可供 Android 物理设备运行的 APK、覆盖安装并验证启动的基准流程。它适用于本地开发和设备验收，不替代 GitHub Actions 的干净工作区发布，也不表示仅凭编译成功即可完成真机验收。

## 构建目标

- 真机包必须显式选择与设备 ABI 匹配的 Runtime Identifier。当前 ARM64 手机使用 `android-arm64`。
- 同一工作区在通用构建、验证构建和不同 RID 之间切换时，必须先按相同的 Framework、Configuration、RID 和条件属性执行定向清理。
- `dotnet clean` 未指定 `-r android-arm64` 时，不保证清除 `obj/Release/net10.0-android/android-arm64` 中的旧资源。
- 普通生产应用不得启用 `Phase8BValidationMode`；只有 Phase 8B.7 的隔离验收应用才设置 `Phase8BValidationMode=true`，并且清理和构建必须使用相同值。
- 构建过程不得修改版本文件。APK 的版本继续来自仓库统一版本配置和当前 Git 提交。

GitHub Actions 从全新 checkout 执行通用 Android publish，通常不存在本地增量目录污染。长期复用的本地工作区则必须遵循本文的定向清理流程。CI artifact 仍需经过物理设备验收，不能因来自干净 runner 就视为已经可运行。

## ARM64 Release 基准命令

以下命令在仓库根目录执行。示例 Android SDK 路径是当前开发环境的 `E:\AndroidSDK`；其他开发机应替换为其实际 SDK 路径。

```powershell
$project = '.\FurinaChronicle.App\FurinaChronicle.App.csproj'
$androidSdk = 'E:\AndroidSDK'

dotnet clean $project `
  -f net10.0-android `
  -c Release `
  -r android-arm64 `
  -p:AndroidSdkDirectory=$androidSdk `
  --verbosity minimal

dotnet build $project `
  -f net10.0-android `
  -c Release `
  -r android-arm64 `
  -p:AndroidSdkDirectory=$androidSdk `
  --verbosity minimal
```

如果依赖已完成还原，可为第二条命令增加 `--no-restore`。不得为了省略一次定向清理而直接复用不确定来源的旧 APK。

Phase 8B.7 隔离验收包在两条命令中均额外加入：

```powershell
-p:Phase8BValidationMode=true
```

构建成功后，ARM64 签名 APK 位于：

```text
FurinaChronicle.App\bin\Release\net10.0-android\android-arm64\com.companyname.furinachronicle.app-Signed.apk
```

不要仅按文件名选择 `bin/Release/net10.0-android` 根目录或旧 `publish` 目录中的 APK。选择产物时同时核对完整路径、最后写入时间、大小和 SHA-256：

```powershell
$apk = Resolve-Path '.\FurinaChronicle.App\bin\Release\net10.0-android\android-arm64\com.companyname.furinachronicle.app-Signed.apk'
Get-Item $apk | Select-Object FullName, Length, LastWriteTimeUtc
Get-FileHash $apk -Algorithm SHA256
```

## 覆盖安装且保留数据

先列出设备并使用 ADB serial 明确选择物理设备。不要依赖会随连接变化的 transport ID，也不要在文档或提交中记录真实设备 serial。

```powershell
$adb = 'E:\AndroidSDK\platform-tools\adb.exe'
& $adb devices -l

$serial = '<physical-device-serial>'
& $adb -s $serial install -r $apk
```

`install -r` 在 application ID 和签名兼容时覆盖安装并保留应用数据。为保护现有档案：

- 不先执行 `adb uninstall`；
- 不执行 `pm clear`；
- 不使用新的临时签名覆盖由另一签名安装的应用；
- 不使用 `-d` 绕过版本降级限制，除非已经明确接受降级风险并准备好一致备份。

如果覆盖安装报告签名不兼容，应停止并查明现有包与新包的签名来源。卸载重装会删除当前设备上的本地应用数据，不能作为默认修复方法。

## 启动与崩溃检查

安装成功只证明 Package Manager 接受 APK。至少还要验证冷启动、前台 Activity、进程存活和无启动崩溃：

```powershell
$package = 'com.companyname.furinachronicle.app'
$activity = (& $adb -s $serial shell cmd package resolve-activity --brief $package |
  Select-Object -Last 1).Trim()

& $adb -s $serial logcat -c
& $adb -s $serial shell am force-stop $package
& $adb -s $serial shell am start -W -n $activity
Start-Sleep -Seconds 10

& $adb -s $serial shell pidof $package
& $adb -s $serial shell dumpsys activity activities |
  Select-String 'mResumedActivity|topResumedActivity'
& $adb -s $serial logcat -d -b crash -v brief
```

验收结论应分层记录：

1. `Build verified`：指定 RID 的 Release APK 编译完成；
2. `Install verified`：目标物理设备上的覆盖安装成功；
3. `Physical device startup verified`：冷启动返回成功、进程仍存活、主 Activity 位于前台且 crash buffer 没有本次启动异常；
4. `Manual workflow verified`：维护者实际完成相应页面、文件选择、数据库升级、导入导出或重开流程。

前三项不能代替第四项。自动化测试、模拟器、ADB 启动证据和人工业务验收必须分别记录。

## 已知的 RID 资源陈旧故障

曾在通用 Android Release 编译成功后出现以下真机启动异常：

```text
System.MissingMethodException: Method not found:
int .Style.get_Maui_Material3_Theme_NoActionBar()
```

对应证据是通用中间目录的 `R.txt` 已包含 `Maui_Material3_Theme_NoActionBar`，但旧的 `obj/Release/net10.0-android/android-arm64/R.txt` 缺少该资源。APK 因此可以编译、签名和安装，却在 MAUI 初始化时崩溃。

处理方法是按本文命令对 `android-arm64` 执行定向 clean 后重新 build，而不是卸载应用或清除数据。需要诊断同类问题时可检查：

```powershell
Select-String `
  '.\FurinaChronicle.App\obj\Release\net10.0-android\android-arm64\R.txt' `
  'Maui_Material3_Theme_NoActionBar'
```

该检查用于定位已知 MAUI 资源失配，不是替代实际启动验证的长期接口契约。MAUI 或资源名称变化后，应以当时的首个真实启动异常和对应 RID 中间产物为准。

## 证据与发布纪律

- 每份真机证据至少绑定源码 commit、dirty 状态、完整应用版本、目标 ABI、APK 大小、SHA-256、签名来源和验证日期。
- 对网站或长期测试渠道发布时使用稳定签名；开发签名只适用于临时测试，不能承诺跨构建机覆盖安装。
- 设备 serial、真实业务数据、UID、Cookie、SToken、AuthKey、SAF URI 和本机私有路径不得进入提交或公开日志。
- 发现不可启动产物时将其明确隔离并标记为不可安装，避免仅凭相同文件名再次分发。
- 网站部署、GitHub artifact 和本地真机包应以 SHA-256 关联，不能只用“最新 APK”作为身份。
