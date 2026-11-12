# WinUI 发布与默认入口

状态：Accepted（2026-11-13，维护者决策 D1/D5/D6/D12）。

## 默认入口

Windows 的默认产品入口是独立 WinUI 3 宿主 `FurinaChronicle.Windows`：

- 发布形态：`win-x64`、self-contained、**unpackaged**（解压即用，无安装器）；
- 数据目录：默认仍是隔离数据目录；首次启动时检测生产数据并引导用户确认“复制迁移”；
- 旧 MAUI Windows 宿主（`FurinaChronicle.App` 的 Windows 目标）**保留一个发布周期**用于回退，
  artifact 名为 `FurinaChronicle-windows-legacy-win-x64-<commit>`；
- 不改变应用标识 / `PublisherDisplayName` / `ApplicationId` / 打包身份，避免数据路径漂移。

决定与约束见 [UI-6 生产切换评审](UI6_PRODUCTION_SWITCH_REVIEW.md)；生产库迁移、凭据重新授权、
单实例与回退验收完成前，不得把隔离数据目录替换为生产目录。

## 本地发布

```powershell
# 默认输出 artifacts/windows-winui/win-x64 并生成 zip
pwsh -File .\eng\Publish-WinUI.ps1

# 指定输出目录（CI 用法：不生成 zip）
pwsh -File .\eng\Publish-WinUI.ps1 -OutputDirectory .\artifacts\windows-winui\win-x64 -NoArchive
```

脚本会：self-contained 发布 WinUI 宿主 → 复制 `README.md`/`LICENSE.txt`/`THIRD-PARTY-NOTICES.txt`
→ 生成 `BUILD-INFO.txt`（版本/提交/运行时/隔离说明）与 `SHA256SUMS.txt` → 可选压缩为 zip。
版本号来自 `Version.props`，发布构建必须使用可靠版本（`FurinaRequireReliableVersion=true`）。

默认 Windows 宿主的 CI artifact 为 `FurinaChronicle-windows-win-x64-<commit>`。旧 MAUI 宿主
继续独立发布为 `FurinaChronicle-windows-legacy-win-x64-<commit>`。

## 安装与升级

1. 将 zip 解压到独立版本目录（例如 `FurinaChronicle\0.10.0\`），不要覆盖正在运行的目录；
2. 运行 `FurinaChronicle.Windows.exe`；
3. 升级时保留旧版本目录一个发布周期，出现问题时可直接回退；
4. 数据目录独立于程序目录；升级/回退不移动数据目录，迁移只在用户确认时发生。

## 首次启动与生产迁移（预期流程，UI-6a 实现并验收）

1. 只读探测生产存储（路径由应用标识派生），展示库大小/时间与备份选项；
2. 用户确认 → 只读校验源库 → **强制备份**（源库保持不变）→ 复制到新宿主数据目录 →
   副本校验（完整性/计数/Schema）→ 写迁移标记；
3. 凭据不迁移：提示用户在新宿主“登录账号”重新授权（手动 SToken/MID 或后续登录入口）；
4. 任一步失败：不切换、源库与目标不变、显示可重试原因；
5. 回退：源库与旧宿主保留，恢复旧版本目录即可继续使用旧宿主；新库不删除、不自动覆盖源库。

单实例、数据目录 `session.lock`、旧进程检测是迁移前置条件；生产库迁移前必须完成迁移演练与
验收清单，真实凭据/网络成功路径仍需授权环境实测。

## 验收清单（发布前）

- [ ] `dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release -m:1` 全通过；
- [ ] `eng/Publish-WinUI.ps1` 发布成功、0 警告 0 错误；
- [ ] 解压到干净目录可启动，窗口标题含版本，隔离数据目录可正常创建；
- [ ] 首次启动迁移向导：无生产数据/有生产数据/迁移成功/迁移失败/已迁移/回退/旧宿主占用；
- [ ] 单实例与 `session.lock`：第二实例不双写、陈旧锁可恢复；
- [ ] 迁移前后计数/哈希/`quick_check` 一致，回退演练通过；
- [ ] 日志与诊断不含用户绝对路径、凭据、完整 URL；
- [ ] Android 最终回归仍通过（Emulator）。

## 已知功能差距（决策 D12：接受并保留旧宿主）

独立 WinUI 当前没有领域在线采集（角色/武器、挑战、深境螺旋）、米游社/HoYoLAB 登录、
Snap.Metadata 显示元数据；Scoped Replace 与 Merge 已交付，恢复点仍为只读摘要。旧 MAUI 宿主
在保留期内可继续承担这些能力；补齐或移除旧宿主需再次评审。
