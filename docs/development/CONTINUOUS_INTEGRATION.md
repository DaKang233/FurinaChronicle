# 持续集成与发布构建

状态：Accepted

## 工作流

`.github/workflows/build-and-test.yml` 在以下情况下运行：

- 向仓库推送提交；
- 创建或更新拉取请求；
- 在 GitHub Actions 页面手动运行。

工作流首先在 Linux 执行 `FurinaChronicle.Tests` 的 Release 测试。测试通过后，分别在 Windows 托管运行器上生成：

- **默认 Windows 宿主**：独立 WinUI 3 `FurinaChronicle.Windows` 的 `win-x64`、自包含且不打包的 Release 应用，由 `eng/Publish-WinUI.ps1` 统一发布；
- **旧 MAUI Windows 宿主**：`net10.0-windows10.0.19041.0`、`win-x64`、自包含且不打包的 Legacy 产物，保留一个发布周期用于回退；
- `net10.0-android`、内嵌程序集的 Android Release APK。

WinUI 宿主默认读取隔离数据目录；生产库迁移必须在首次启动时由用户确认，按 [UI-6 生产切换评审](../project/UI6_PRODUCTION_SWITCH_REVIEW.md) 的复制迁移方案执行。旧 MAUI 宿主在保留期内仍可打开原有生产数据，二者不得同时写同一数据目录。独立 Android UI-1 验收包的本地命令见 [UI-1 DSH 验收指南](UI1_DSH_ACCEPTANCE_GUIDE.md)，普通 Android CI 包仍走现有应用入口。

仓库根目录的 `global.json` 将 SDK 限定在 .NET 10，并允许使用当前 .NET 10 的最新功能带和补丁版本，避免托管运行器预装更高主版本 SDK 后错误选择其他 SDK 或 MAUI 工作负载。

所有 job 均以 `fetch-depth: 0` 检出完整 Git 历史，使 Windows 与 Android 从同一 `HEAD` commit count 得到相同 Build。发布命令设置 `FurinaRequireReliableVersion=true`；如果 checkout 意外变为 shallow、Git 不可用且发布流程没有显式提供可靠 Build，工作流必须失败。Android 不再使用 `github.run_number` 维护第二套 versionCode。完整规则见[版本策略](VERSIONING.md)。

测试结果保留 14 天，Windows 与 Android 构建产物保留 30 天。构建通过只证明自动化测试和编译发布成功，不代替 Windows、Android 真机运行验收。

Phase 8C 的本地 Windows 进程和 Android 真机证据由独立验收工具保存。Android 探针使用专用包名，按本地 [8C 验收工具说明](../../eng/phase8c/README.md) 构建和运行。CI 应用发布物不包含该探针。实际运行、指纹及当前 CI 状态见 [Phase 8C 退出评审](../project/PHASE_8C_EXIT_REVIEW.md)。

本地生成适用于物理设备的 APK 时，必须显式选择设备 ABI、清理对应 RID 的中间产物、覆盖安装并检查真实启动。完整命令、保留数据约束和已知 MAUI RID 资源陈旧故障见 [Android 真机构建与部署](ANDROID_PHYSICAL_DEVICE_DEPLOYMENT.md)。GitHub Actions 的干净工作区发布与本地长期复用工作区的定向清理是两种不同场景。

## Android 签名

工作流支持以下仓库 Actions secrets：

- `ANDROID_KEYSTORE_BASE64`：密钥库文件的 Base64 内容；
- `ANDROID_KEY_ALIAS`：密钥别名；
- `ANDROID_KEY_PASSWORD`：密钥密码；
- `ANDROID_STORE_PASSWORD`：密钥库密码。

四项必须同时配置。工作流不会把密码作为命令行明文传给构建工具。正式密钥仅在手动运行工作流或推送 Git tag 时注入 Android 发布步骤；普通分支推送和拉取请求不会获得正式签名材料。

如果没有配置任何签名 secret，.NET Android SDK 仍会生成带开发签名的可安装 APK。该产物只用于测试：不同 GitHub Actions 运行生成的开发签名可能不同，因此不能承诺直接覆盖安装此前的 APK。面向长期分发或网站部署时必须配置并安全备份固定的正式密钥库；密钥库及密码不得提交到仓库。

Windows PowerShell 可用下列命令生成密钥库文件的 Base64 文本，再将结果保存到 GitHub secret：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('FurinaChronicle.keystore'))
```

## 发布物

每次成功运行会生成三个应用 artifact：

- `FurinaChronicle-windows-win-x64-<commit>`：**默认 Windows 宿主**（独立 WinUI 3，unpackaged、自包含）；
- `FurinaChronicle-windows-legacy-win-x64-<commit>`：旧 MAUI Windows 宿主，保留一个发布周期用于回退；
- `FurinaChronicle-android-<commit>`。

默认 Windows 宿主的本地等价发布命令见 [WinUI 发布与默认入口](../project/UI6_RELEASE_PACKAGING.md)；发布脚本会生成 `BUILD-INFO.txt` 与 `SHA256SUMS.txt`。Android artifact 同时包含 `SHA256SUMS.txt`。三个 artifact 均附带仓库的 `README.md`、`LICENSE.txt` 与 `THIRD-PARTY-NOTICES.txt`。

工作流只生成 GitHub Actions artifact，不会自动创建 GitHub Release，也不会自动部署到网站。公开发布仍是独立、可审计的操作。
