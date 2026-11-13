# WinUI 发布与默认入口

状态：Accepted（2026-11-13，维护者决策 D1/D5/D6/D12）。

## 默认入口

Windows 的默认产品入口是独立 WinUI 3 宿主 `FurinaChronicle.Windows`：

- 发布形态：`win-x64`、self-contained、**unpackaged**（解压即用，无安装器）；
- 数据目录：默认直接使用与旧 MAUI Windows 宿主相同的生产数据库（就地读写）；首次启动只读校验、强制备份并确认接管；显式 `FURINA_UI1_DIRECTORY` 时进入隔离/测试模式；
- 旧 MAUI Windows 宿主（`FurinaChronicle.App` 的 Windows 目标）**长期保留**，仅用于兼容性、基本功能查看与验证，不进入 CI 产物、没有专门 UI 优化；
- 不改变应用标识 / `PublisherDisplayName` / `ApplicationId` / 打包身份，避免数据路径漂移。

决定与约束见 [UI-6 生产切换评审](UI6_PRODUCTION_SWITCH_REVIEW.md)；生产库就地使用、凭据重新授权、
单实例与旧宿主检测、备份与回退验收完成前，不得把默认行为切换到生产目录。

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

Windows CI 只产出一个 artifact：`FurinaChronicle-windows-win-x64-<commit>`（WinUI）；旧 MAUI 宿主仅保留源码供本地兼容性构建。

## 安装与升级

1. 将 zip 解压到独立版本目录（例如 `FurinaChronicle\0.10.0\`），不要覆盖正在运行的目录；
2. 运行 `FurinaChronicle.Windows.exe`；
3. 升级时保留旧版本目录一个发布周期，出现问题时可直接回退；
4. 数据目录独立于程序目录；升级/回退不移动数据目录，迁移只在用户确认时发生。

## 首次启动与生产数据（同一数据库）

WinUI 宿主直接使用与旧 MAUI Windows 宿主**相同的生产数据库文件**（就地读写，不复制到其他目录）：

1. 只读探测生产数据路径与库元数据（不读内容）；显式设置 `FURINA_UI1_DIRECTORY` 时进入隔离/测试模式，不探测生产；
2. 首次使用现有生产库：只读校验（application_id / Schema / `quick_check`）→ **强制备份**到生产数据目录 → 写脱敏接管标记 → 以同一数据库启动；失败不写标记、不修改原库；
3. 无现有生产库时按 MAUI 语义初始化新库（不播种合成数据）；
4. 凭据不迁移：用户在新宿主“登录账号”重新授权；
5. 回退：关闭 WinUI，继续使用旧 MAUI 宿主（同一数据库）；备份与接管标记保留。
6. **不得同时运行两个宿主**：WinUI 启动时检测旧 MAUI 进程并阻止写入；旧宿主不识别 WinUI 的 session.lock，因此需要用户先关闭其中一个。

旧 MAUI Windows 宿主长期保留，用于兼容性、基本功能查看与验证，不提供专门 UI 优化；CI 不再构建 MAUI Windows x64，需要时本地手动构建。

单实例、数据根 `session.lock` 与旧进程检测是前置条件；真实凭据/网络成功路径仍需授权环境实测。

## 验收清单（发布前）

- [ ] `dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release -m:1` 全通过；
- [ ] `eng/Publish-WinUI.ps1` 发布成功、0 警告 0 错误；
- [ ] 解压到干净目录可启动，窗口标题含版本，隔离数据目录可正常创建；
- [ ] 首次启动接管：无生产库初始化/现有库校验+备份+标记成功/校验失败不改库/幂等/回退/旧宿主占用；
- [ ] 单实例与 `session.lock`：第二实例不双写、陈旧锁可恢复；
- [ ] 迁移前后计数/哈希/`quick_check` 一致，回退演练通过；
- [ ] 日志与诊断不含用户绝对路径、凭据、完整 URL；
- [ ] Android 最终回归仍通过（Emulator）。

## 已知功能差距（决策 D12：接受并保留旧宿主）

独立 WinUI 当前没有领域在线采集（角色/武器、挑战、深境螺旋）、米游社/HoYoLAB 登录、
Snap.Metadata 显示元数据；Scoped Replace 与 Merge 已交付，恢复点仍为只读摘要。旧 MAUI 宿主
在保留期内可继续承担这些能力；补齐或移除旧宿主需再次评审。
