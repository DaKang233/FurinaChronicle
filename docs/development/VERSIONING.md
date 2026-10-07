# FurinaChronicle 版本策略

状态：Accepted

## 目标

FurinaChronicle 的 Windows 与 Android 应用共用一个可审计的版本来源。版本构建不得改写仓库文件，同一 Git 提交无论重复 Build、Clean、Rebuild、Publish 或 adb 部署，都必须得到相同的数字版本。

版本构成如下：

```text
Major.Minor.Patch.Build
```

项目规划标签独立保存：

```text
Version    = 0.8.5.94
PhaseLabel = 8B.4
Commit     = 75790ba
```

`PhaseLabel` 不是软件版本的第四段，也不得把 `A / B / C` 编码进 `Patch`。

## 各段含义

- `Major`：产品成熟代际；正式 1.0 之前固定为 `0`。
- `Minor`：大的开发 Phase；Phase 8 为 `8`，Phase 9 为 `9`。
- `Patch`：同一大 Phase 内单调递增的软件实施或发布里程碑序号。
- `Build`：当前源码状态的构建标识；正常 Git 工作树取当前 `HEAD` 的 commit count。
- `PhaseLabel`：项目管理阶段，例如 `8B.3`；它不参与数字版本计算。

`Build` 不表示本地执行了多少次编译。按编译次数递增会使同一源码在开发机、CI、Windows 和 Android 上产生不同版本，并要求构建过程修改受版本控制的文件，因此禁止采用。

## 人工维护的唯一来源

仓库根目录的 `Version.props` 是人工维护的版本来源：

```xml
<FurinaVersionMajor>0</FurinaVersionMajor>
<FurinaVersionMinor>8</FurinaVersionMinor>
<FurinaVersionPatch>5</FurinaVersionPatch>
<FurinaPhaseLabel>8B.4</FurinaPhaseLabel>
```

`build/FurinaVersion.targets` 只读取这些值，并解析 Git 或 CI 显式输入；不得回写 `Version.props`。

修改责任如下：

- 普通提交：不修改 `Version.props`；只有 Git commit count 使 `Build` 自然增加。
- 新的软件里程碑：人工增加 `FurinaVersionPatch`，并按实际计划更新 `FurinaPhaseLabel`。
- 进入新的子阶段：`Patch` 继续增加，不因 `8B → 8C` 重置。
- 进入新的大 Phase：增加 `FurinaVersionMinor`，并可将 `FurinaVersionPatch` 重置为 `0`。
- 正式 1.0 前：`FurinaVersionMajor` 保持 `0`。

项目计划拆分、插入或重新编号时，不重写已经发布的软件版本；继续增加 `Patch` 并更新独立的 `PhaseLabel`。

## Phase 与 Patch 示例

Phase 8 内的映射可以为：

| PhaseLabel | 软件版本前缀 |
| --- | --- |
| 8A 完成 | `0.8.0` |
| 8B.0 | `0.8.1` |
| 8B.1 | `0.8.2` |
| 8B.2 | `0.8.3` |
| 8B.3 | `0.8.4` |
| 8B.4 | `0.8.5` |
| 8B.7 | `0.8.8` |
| 8C.0 | `0.8.9` |
| 8C.1 | `0.8.10` |

因此 `8B.x` 与 `8C.x` 不复用 `Major.Minor.Patch`。`Patch` 不要求与 PhaseLabel 最后一段相等；映射一旦发布，只能向前增加，不能重编号历史。

进入 Phase 9 时可以使用 `0.9.0.x`。

## Git 派生规则

构建目标取得：

- `git rev-list --count HEAD`：可靠 Build；
- `git rev-parse --short=7 HEAD`：短 SHA；
- `git status --porcelain`：开发诊断用 dirty 状态；
- `git rev-parse --is-shallow-repository`：判断 commit count 是否完整。

dirty 状态不改变 `Build`、Android `versionCode` 或正式数字版本。同一 `HEAD` 的干净与脏工作树具有相同 `Major.Minor.Patch.Build`，只在诊断元数据中不同。

commit count 是当前构建实现，不是 Furina 可移植数据、备份或同步协议中的永久字段。

## 平台映射

两平台均由 `FurinaChronicle.App.csproj` 导入同一个 `Version.props` 和 `build/FurinaVersion.targets`。

### Android

| Android / MAUI 属性 | 值 |
| --- | --- |
| `ApplicationDisplayVersion` / `versionName` | `Major.Minor.Patch` |
| `ApplicationVersion` / `versionCode` | `Build` |

例如完整版本 `0.8.4.94` 映射为 `versionName=0.8.4`、`versionCode=94`。`Build` 必须是 `1..2100000000` 的整数。

### Windows 与 .NET 程序集

当前 Windows 产物为 `WindowsPackageType=None` 的 unpackaged 应用，因此没有独立发布的 MSIX Package Version。实际程序集映射为：

| MSBuild / 程序集属性 | 值 |
| --- | --- |
| `ApplicationDisplayVersion` | `Major.Minor.Patch` |
| `ApplicationVersion` | `Build` |
| `Version` | `Major.Minor.Patch.Build` |
| `AssemblyVersion` | `Major.Minor.Patch.0` |
| `FileVersion` | `Major.Minor.Patch.0` |
| `InformationalVersion` | 完整版本、Phase、SHA 与可选 dirty 标记 |

MAUI 生成的 Windows 中间 Appx manifest 使用 `Major.Minor.Patch.Build`，所以 Windows `AppInfo.Version` 会反映完整四段版本；当前 unpackaged 发布不分发该 manifest。Furina 自己的关于页或用户界面应使用 `ApplicationVersionInfo.DisplayVersion` 显示与 Android 一致的三段版本，并把四段版本放在诊断信息中。

`AssemblyVersion` 和 `FileVersion` 的第四段固定为 `0`，避免把 Git commit count 强行塞入传统 Windows 四段版本的 16 位组件限制，并保持程序集绑定身份稳定。完整可诊断版本不丢失：构建同时写入 `AssemblyInformationalVersion` 和 `AssemblyMetadata`。

运行时可通过 `ApplicationVersionInfo` 取得：

- 完整版本；
- 用户可见版本；
- PhaseLabel；
- commit；
- dirty 状态；
- 版本来源及是否可用于正式发布。

如果未来改为 MSIX 打包，必须在实施时单独记录 Package Version 的平台范围约束，但不得另设人工版本源。

## Git 不可用、源码压缩包与 shallow clone

版本来源按以下优先级解析：

1. 显式 `FurinaBuildNumber`；
2. 完整 Git 历史的 `HEAD` commit count；
3. 明确标记的非发布开发 fallback。

普通开发构建在 Git 不可用、目录不是 Git clone 或仓库为 shallow clone 时使用 `FurinaDevelopmentFallbackBuild`（默认 `1`），同时：

- 输出警告；
- `FurinaVersionSource=development-fallback`；
- `FurinaVersionIsReliable=false`；
- commit/dirty 无法取得时显示 `nogit / unknown`。

这个值只允许本地试编译，不代表正式版本。`dotnet publish` 或设置 `FurinaRequireReliableVersion=true` 时，非可靠 fallback 会直接失败，因而不会静默产生可发布的 Android versionCode。

CI 或发布脚本可以显式传入：

```text
-p:FurinaBuildNumber=<positive integer>
-p:FurinaGitCommit=<short or full SHA>
-p:FurinaGitDirty=false
-p:FurinaRequireReliableVersion=true
```

显式 Build 被视为发布方对其可靠性和单调性的承诺。`FurinaGitCommit` 与 `FurinaGitDirty` 是诊断覆盖，不影响数字版本。

GitHub Actions 使用 `fetch-depth: 0`，不再以 `github.run_number` 作为 Android versionCode。其他 shallow CI 必须获取完整历史，或由可信发布流程显式提供 `FurinaBuildNumber`。

## 验证

本地可运行：

```powershell
./build/Verify-Versioning.ps1
```

脚本在系统临时目录建立隔离 Git fixture，验证：

- 同一提交重复解析不变；
- 新提交增加 Build；
- Windows 与 Android 共享完整版本、显示版本和 Build；
- Patch / PhaseLabel 变化不修改 Build；
- 8B 到 8C 的示例继续增加 Patch；
- 非 Git 源码目录与 shallow clone 只能得到非发布 fallback；
- 严格发布模式拒绝不可靠来源；
- 显式 CI Build 可以在无 Git 历史时使用。

## 1.0 后复审

进入正式 1.0 前，应重新评估 Major/Minor/Patch 的发布兼容语义、Windows 打包方式以及应用商店版本限制。在此之前不得提前把当前 Phase 编号规则宣称为 1.0 后的永久公开协议。
