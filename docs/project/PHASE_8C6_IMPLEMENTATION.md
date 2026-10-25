# Phase 8C.6 双向 Archive 往返工程实施与设备验收状态

状态：Engineering completed；Awaiting device evidence

日期：2026-10-25

版本：`0.8.15 / 8C.6`

数据库：Schema 10

## 范围与结论

本工作包把 8C.5 的正式文件流程推进到可重复审查的双向往返验收基础。当前已经完成：

- 代表性合成档案定义；
- 严格逻辑语义比较器及负向变异测试；
- 两个重开的真实临时 SQLite 数据库之间的双次恢复／再导出往返；
- Archive 保真缺口修复和 Schema 10 迁移；
- 只读宿主验证工具、独立起点样本和资源指标；
- 复用正式 8C.5 页面、但隔离应用标识与数据库的验收构建模式；
- 全量自动化与 Windows／WinUI／Android 适用构建。

尚未完成实际 Windows↔Android 系统文件往返、Android 真机交互、受控 Scoped Replace
和真实进程终止／重开，因此不能把 8C.6 标记为 `Device workflow verified`。当前准确
状态是 `Awaiting device evidence`。

## 代表性样本

受版本控制的 `SyntheticArchiveSnapshot` 生成两份内容与稳定 ID 均独立的起点。每份
至少包含：

- 四个档案：主档案、同 UID 独立副本、空档案和包含 Unresolved 账号的档案；
- 一个空账号；
- Gacha 当前事实及一条来源 Revision；
- Unresolved 账号下无法用 UIGF 完整表达的 Gacha 事实；
- 同一物品 `A → B → A` 的三次不可变观察；
- Week 精度、UTC+8 原始偏移的手工获取及 Revision；
- Partial 深境螺旋观察，最后 Chamber 发生时间保持 Unknown；
- `OfficialApi`、`StandardImport`、`UserEntered`、`FurinaImport` 来源及引用；
- `RequireUniqueArchiveNames` 与各档案 `UndoLimit`。

生成的宿主工程样本位于工作区外，不进入 Git：

| 起点 | SHA-256 | 容器 | 声明展开 |
|---|---|---:|---:|
| `windows-origin.fca` | `5c6a6de7dc4fec28626feff77ef614488885e62204ce0258021d64d9e4ffaf78` | 8,888 B | 24,175 B |
| `android-origin.fca` | `93efde5594320cc2a91c9f8c3c6659e696449ed1f91404d53a4f724e90969b74` | 8,922 B | 24,199 B |

第二份只是“为 Android 方向准备的独立内容”，不是 Android 设备生成证据；只有通过正式
Android 页面导出后的文件才可以称为 Android-origin device evidence。

## 严格语义比较

`ArchiveSemanticComparer` 读取经过正式 Reader／Validator／staging 的两个包，按多重
集合比较逻辑内容。它只忽略：

- 目标本地档案／账号 ID 和包内重映射引用；
- Manifest 导出时间、生成平台、应用构建和 Commit；
- 接收操作为了记录本次恢复而额外产生的 Revision。

以下仍必须严格一致：

- 档案／账号数量、精确名称、父子归属、空父对象和同 UID 独立副本；
- 共享身份 GUID、自然身份和 Unresolved 字段；
- 当前事实、稳定标识和来源已保留 Revision；
- 时间点／区间、TimePrecision、原始偏移和未知时间；
- provenance、来源引用、完整度和白名单设置。

删除 Revision、改变时间精度、合并同 UID 副本、删除空档案四类变异均返回
`Different`。ZIP SHA-256 不同只说明容器不同，不替代语义结论。

## Schema 10

真实 `Archive → SQLite → Archive` 往返揭示了此前单次 Codec 测试无法发现的承载缺口。
Schema 10 增加：

- `PlayerArchives`、`GameAccounts` 的 Created／Updated offset minutes；
- `GachaRecords.ObservedAtUtcTicks／ObservedAtOffsetMinutes`；
- `GachaRecords` 的 SourceProvider／SourceRecordId／SourceSnapshotId；
- `GachaRevisions` 与 `ArchiveDomainRevisions` 的 CreatedAtOffsetMinutes。

迁移支持 v1～v9 到 v10，并保持 application ID、future schema、损坏库和事务失败保护。
旧数据无法证明的观察时间、来源引用或偏移保持空／既有可证明值，不进行猜测。

往返测试还发现新建 Unresolved 账号在父对象尚未落库时不能查询数据库决定事实版本；
恢复路径现使用已知本地账号范围计算 Tombstone key。完整套件随后发现已有解析账号的
重引入仍需同时读取共享身份 Tombstone 高水位，已补回该分支并由删除／重引入测试覆盖。

## 自动化证据

| 验证 | 结果 |
|---|---|
| 严格比较器正例和四类必需变异 | Pass |
| 代表性包经数据库 A Merge／NoOp／重开／导出 | Pass |
| A 的输出经数据库 B Merge／NoOp／重开／导出 | Pass |
| 起点与 B 返回包严格逻辑比较 | Equivalent |
| Restore Tombstone／Replace／取消／NoOp／故障回滚既有套件 | Pass |
| Schema v1～v9 → v10、future／application ID／损坏／失败回滚 | Pass |
| Release 全量测试 | 605 passed，0 failed，0 skipped |
| `git diff --check` | Pass；仅工作树换行转换提示 |

宿主工具对 `windows-origin.fca` 自比较的当前测量为：

- elapsed：约 520 ms；
- staging：每侧 36 KiB；
- 临时目录 sampled peak：72 KiB；
- process working set：22.21 MiB baseline / 51.35 MiB sampled peak。

该测量用于回归和证据格式验证，不是手机性能承诺。Preview、Apply、重开、导出及稳定后
内存仍须在各实际设备流程中分别记录。

## 适用构建

| 目标 | 命令边界 | 结果 |
|---|---|---|
| MAUI Windows x64 Release | `net10.0-windows10.0.19041.0`、unpackaged 参数 | 0 warning，0 error |
| 独立 WinUI 3 UI-0 x64 Release | `FurinaChronicle.Windows` | 0 warning，0 error |
| Android Release APK | `net10.0-android`、`E:\AndroidSDK`、程序集内嵌 | 0 warning，0 error；约 2m56s |
| 8C.6 Windows 隔离验收构建 | `Phase8CValidationMode=true`、正式 UI | 0 warning，0 error |
| 8C.6 Android 隔离验收 APK | 独立 package、正式 UI | 0 warning，0 error；约 2m38s |
| CI | 未推送 | Not run |

构建成功不等于 Windows 可见交互、Android 安装启动或真机文件往返通过。

`Phase8CValidationMode` 不启用旧的 8B.7 专用页面。它保留正式 8C.5 导出、Reader、
Preview、Merge／Scoped Replace 和结果页面，只改变：

- 标题为 `FurinaChronicle 8C.6 Validation`；
- Android package 为
  `com.companyname.furinachronicle.validation.phase8c6`，可与主应用并存；
- 数据库为 app-private `phase8c6-validation/furinachronicle-phase8c6-validation.db3`；
- Windows 可用 `FURINA_PHASE8C6_DIRECTORY` 指向明确的绝对验收目录。

8B.7 与 8C.6 两个验收模式同时启用时构建明确失败。APK badging 已确认独立包名、标题、
`versionName 0.8.15` 和 Git 派生整数 versionCode。当前没有 ADB 设备连接，未安装或
启动该 APK，仍不产生真机证据。

## 验证工具

`eng/phase8c/FurinaChronicle.Phase8CValidation` 提供：

```powershell
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- generate windows <output.fca>
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- generate android <output.fca>
dotnet run --project .\eng\phase8c\FurinaChronicle.Phase8CValidation -- compare <origin.fca> <returned.fca> --report <report.md>
```

工具只生成合成起点或读取 Archive；它不打开生产数据库，不执行 Apply，也不绕过 8C.5
正式页面。报告包含两侧容器／展开／staging 大小、临时峰值、working set、耗时和逐路径
语义差异。

## 提交

| Commit | 内容 |
|---|---|
| `8b8259d` | 冻结 8C.6 验收契约和版本里程碑 |
| `ab82736` | 严格语义比较器、正例和变异负例 |
| `9518bb0` | Schema 10 保真承载、迁移和双数据库往返 |
| `aeee41d` | 独立样本、指标与返回包验证工具 |
| `689bac3` | 已有账号重引入的共享身份 Tombstone 版本高水位回归修复 |

## 必须人工完成的设备验收

### Windows 发起

1. Windows 隔离实例通过正式页面导出起点；
2. Android 真机通过系统文件提供器选择、Preview、确认 Merge；
3. 关闭并重开 Android 应用，确认状态／Receipt；
4. 再次导入同一包并确认 NoOp；
5. 在 Android 导出返回包；
6. Windows 使用严格工具比较起点与返回包。

### Android 发起

1. Android 隔离实例独立建立内容并通过正式页面导出；
2. Windows 正式页面 Preview／Merge，关闭并重开；
3. Windows 重复导入确认 NoOp，再导出返回包；
4. Android 或宿主严格工具比较起点与返回包。

### 两端均需

- 系统选择器取消；
- Preview 后拒绝，确认零写入且 staging 清理；
- 已有目标数据和活动 Tombstone 的绑定确认；
- 在一次性受控范围执行一个 Archive／Account／Domain Scoped Replace；
- 记录 Preview、Apply、重开、导出、比较耗时和进程内存；
- 不使用主业务数据库，不测试未授权的恢复点回切。

完成上述两个方向、成功／NoOp／取消／拒绝、受控 Replace、重开和严格比较后，8C.6
才可从 `Awaiting device evidence` 改为 `Device workflow verified`。

## 后续边界

8C.6 不实现主库热替换／恢复点回切、云同步、ChangeJournal、WebDAV、E2EE、在线
Provider、UIAF 成就或新的公开 Archive major。设备证据完成后，8C.7 可以据此执行
Phase 8C 退出审查、兼容性标记和发布前文档收敛。
