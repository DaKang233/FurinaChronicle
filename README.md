# FurinaChronicle

FurinaChronicle 是一个以本地数据为核心的原神个人历史档案应用。它用于持续保存、交换和分析官方服务不会长期保留的个人游戏数据，而不是替代游戏启动器、安装器或修复工具。

当前优先支持 Windows 与 Android。没有 FurinaChronicle 服务端时，核心采集、归档、查询、导入、导出和备份能力仍应正常工作。

开始开发或使用 Agent 修改本仓库前，请先阅读：

1. [项目文档索引](docs/README.md)
2. [项目宪章](docs/project/PROJECT_CHARTER.md)
3. [数据原则](docs/principles/DATA_PRINCIPLES.md)
4. [Agent 与开发约束](AGENTS.md)

文档中标记为“目标模型”的内容不表示数据库迁移已经实现。

## 构建与运行

需要 .NET 10 SDK、Git；Windows 额外需要 Windows App SDK 运行环境（发布物已自包含）与 .NET MAUI workload（仅构建旧宿主或 Android 时）。

```powershell
# 自动化回归
dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release -m:1 --nologo

# Windows 默认宿主：独立 WinUI 3（unpackaged、自包含）
pwsh -File .\eng\Publish-WinUI.ps1
.\artifacts\windows-winui\win-x64\FurinaChronicle.Windows.exe

# 旧 MAUI Windows 宿主（保留一个发布周期，用于回退）
dotnet publish FurinaChronicle.App/FurinaChronicle.App.csproj `
  -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 `
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true

# Android Emulator（x64 验证包）
dotnet build FurinaChronicle.App/FurinaChronicle.App.csproj -f net10.0-android -c Release `
  -r android-x64 -p:AndroidSdkDirectory=<Android SDK 路径> -p:Phase8CValidationMode=true `
  -p:EmbeddedAssembliesIntoApk=true
```

Windows 默认入口、生产库迁移、回退与已知功能差距见 [WinUI 发布与默认入口](docs/project/UI6_RELEASE_PACKAGING.md)。首次启动默认使用隔离数据目录；生产数据迁移必须在迁移向导中由用户确认。
