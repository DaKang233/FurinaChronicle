# Phase 8C.5 双端产品文件流程与可读出口实施记录

状态：Implementation complete；automated verified；build verified；awaiting device evidence

日期：2026-10-25（Asia/Shanghai）

版本：`0.8.14 / 8C.5`

数据库：Schema 9（本工作包无迁移）

## 结论

8C.5 已在现有 MAUI Windows／Android 宿主提供正式“备份与恢复”入口，不再要求用户使用
CLI 或 `PHASE8B_VALIDATION` 隐藏验收页。产品流程直接复用 8C.1～8C.4 的
`IArchiveLogicalExporter`、`IArchiveRestoreInputStager`、`IArchiveRestorePlanner`
和 `IArchiveAtomicRestorer`，没有建立第二套 SQL 或逐领域 Apply。

本记录关闭代码、自动化和适用构建范围；尚未取得系统文件选择器实际交互、Windows 与
Android 双向文件往返、真实进程终止／重开或受控生产数据库副本证据，因此 8C.5 状态不是
`Device workflow verified`，Phase 8C 也尚未关闭。

## 正式产品入口

- Windows：现有 Shell 侧栏新增“备份与恢复”。
- Android：从“档案与账号管理”进入完整预览／确认页，不增加第六个底部页签。
- 页面明确区分完整逻辑 Archive、分类 Portable、UIGF 和 Markdown，并显示是否可重新
  导入及主要损失边界。
- 页面可导出全部档案或当前所选档案的 `.fca`；分类 Portable／Markdown 和抽卡
  Markdown 使用页面自己的档案选择，不改写其他业务页选择。
- UIGF 与 Gacha Portable 继续进入原抽卡页，保持 ADR 0015 的统一全量模式和现有导入
  语义。

`.fca` 先写入 app-private 临时文件，再以流交给 Windows FileSavePicker 或 Android
系统文档提供器；成功、取消和失败均执行有界临时文件清理。Android 返回值只用于显示，
不会作为普通路径、SAF 权限或 Archive 数据保存。

## Preview 与恢复

正式流程为：

1. 系统 picker 提供输入流；
2. 输入复制到 app-private staging，完成哈希、Reader／Validator 和私有索引；
3. 用户选择 Merge 或 Scoped Replace；
4. Replace 显式选择一个 Archive、Account 或 Domain 范围；
5. 名称歧义和 Unresolved 账号要求用户选择目标或创建新副本；
6. 页面显示父对象映射、各领域 Add／Keep／Replace／Remove、冲突、Tombstone、
   设置差异、Undo 边界和空间估算；
7. 用户接受绑定当前 Preview 的确认项后，只有无冲突计划才能进入原子 Apply。

首版正式 UI 每次 Replace 操作选择一个明确范围；底层 Plan／Apply 仍支持多个互不重叠
范围，UI 没有缩减或改变其数据语义。需要多个不相交范围时，当前产品流程分次预览和提交。

Apply 使用一次预览会话内稳定的 `OperationId`。调用抛出异常时，ViewModel 先通过
`FindReceiptAsync` 查询持久化 Receipt；找到即按 AlreadyCommitted 显示，未找到才报告
“结果未知”，不自动换 OperationId 重复提交。空间不足时只在用户确认后携带现有
`HistoryCleanupPlan.ConfirmationId` 重试。

提交成功后的档案列表或页面刷新失败会显示“业务提交已完成，但页面刷新失败”，不会把
事务描述为失败或试图回滚。退出页面、取消或选择新文件会释放 staging；进程重启后不复用
内存 Plan，必须重新选择并预览。

## 恢复点边界

页面只显示当前 Replace 恢复点的创建时间、大小、Schema 和校验状态。没有“恢复此恢复点”
按钮。设备本地一致 SQLite 副本仍不是公开 Archive，也不跨端；主库回切继续需要独立的
连接关闭、WAL／SHM、同卷原子切换和回滚契约。

## Gacha Markdown

新增两个正式入口：

- 摘要：档案／账号范围、记录数、时间范围、卡池／星级计数、来源分布和完整性声明；
- 明细：逐条记录账号、外部记录 ID、发生时间、原始 UTC 偏移、物品、星级、类型、卡池、
  数量、来源、观察／抓取／导入时间和有限来源引用。

缺失字段显示“未知”；导出时间不会补业务时间。报告使用领域 Gacha 事实而不是 Chronicle，
不依赖在线图片，并明确不能重新导入、不能替代 UIGF 或完整逻辑 Archive。

## 自动化与构建证据

| 验证 | 状态 |
|---|---|
| Gacha Export 定向测试 | Passed；14/14 |
| Archive Portable／Restore 定向测试 | Passed；65/65 |
| Release 全量测试 | Passed；596/596，0 failed，0 skipped |
| MAUI Windows Release | Passed；0 warnings，0 errors |
| MAUI Android arm64 Release，`AndroidSdkDirectory=E:\AndroidSDK` | Passed；0 warnings，0 errors |
| 版本脚本／版权／diff | 最终文档提交前复核 |
| CI | Not run；本地提交尚未推送 |
| Windows 系统 picker／saver 实际交互 | Not run |
| Android 系统文档提供器／返回键／后台交互 | Not run |
| Windows↔Android 双向语义往返 | Not run；属于 8C.6 |
| 真实进程终止／重开与 Receipt 恢复显示 | Not run |
| 真实生产数据库 Merge／Replace | Not run；只使用自动化合成临时库 |

## 本地提交

| Commit | 内容 |
|---|---|
| `cfa124b` | 8C.5 版本和双端产品流程契约 |
| `034a4db` | Gacha Markdown 摘要／明细 Writer、用例和测试 |
| `17c7b4e` | 正式备份／恢复 ViewModel、页面、文件边界与双端导航 |

## 8C.6 最短交接

1. 在 Windows 和 Android 各自使用系统 picker／saver 完成成功、取消、错误及重复 NoOp。
2. 使用代表性合成档案执行 Windows→Android→Windows 与 Android→Windows→Android，
   用语义比较器核对允许变化项之外的事实、Revision、归属、偏移、来源、完整性和设置。
3. 对 Merge 与一个明确 Scoped Replace 范围分别取得受控数据库副本证据；不得直接拿
   私人生产库做破坏性实验。
4. 验证页面退出、Android 后台／返回、进程终止前后 Receipt 查询与重新 Preview。
5. 分别记录容器／staging／恢复点空间峰值、耗时和实际进程内存；构建通过不能替代设备
   性能结论。
