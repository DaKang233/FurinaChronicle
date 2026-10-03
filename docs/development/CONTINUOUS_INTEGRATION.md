# 持续集成与发布构建

状态：Accepted

## 工作流

`.github/workflows/build-and-test.yml` 在以下情况下运行：

- 向仓库推送提交；
- 创建或更新拉取请求；
- 在 GitHub Actions 页面手动运行。

工作流首先在 Linux 执行 `FurinaChronicle.Tests` 的 Release 测试。测试通过后，分别在 Windows 托管运行器上生成：

- `net10.0-windows10.0.19041.0`、`win-x64`、自包含且不打包的 Windows Release 应用；
- `net10.0-android`、内嵌程序集的 Android Release APK。

仓库根目录的 `global.json` 将 SDK 限定在 .NET 10，并允许使用当前 .NET 10 的最新功能带和补丁版本，避免托管运行器预装更高主版本 SDK 后错误选择其他 SDK 或 MAUI 工作负载。

测试结果保留 14 天，Windows 与 Android 构建产物保留 30 天。构建通过只证明自动化测试和编译发布成功，不代替 Windows、Android 真机运行验收。

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

每次成功运行会生成两个应用 artifact：

- `FurinaChronicle-windows-win-x64-<commit>`；
- `FurinaChronicle-android-<commit>`。

Android artifact 同时包含 `SHA256SUMS.txt`。两个 artifact 均附带仓库的 `README.md`、`LICENSE.txt` 与 `THIRD-PARTY-NOTICES.txt`。

工作流只生成 GitHub Actions artifact，不会自动创建 GitHub Release，也不会自动部署到网站。公开发布仍是独立、可审计的操作。
