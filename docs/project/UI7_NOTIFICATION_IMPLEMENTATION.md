# UI-7 通知统一与抽卡页精简实施记录

状态：工作包 1、2 已实施并通过桌面验收（2026-11-15）；工作包 3、4 待实施。
计划来源：[WINUI_NOTIFICATION_AND_SIMPLIFICATION_PLAN](WINUI_NOTIFICATION_AND_SIMPLIFICATION_PLAN.md)。

## 工作包 1：窗口级通知体验

- **任务型通知**：`NotificationRequest` 增加 `TaskKey`、`IsInProgress`、`DiagnosticId`；
  `NotificationCenter` 对同一 `TaskKey` 原地更新（保留 Id），进行中不增加未读，完成/失败变为未读；
  移除/清空/淘汰时同步清理任务映射。`NotificationTaskKeys` 提供刷新、导入、导出、记录、启动等稳定键。
- **通知面板**：维持维护者要求的设计——**无通知时面板与铃铛整体隐藏，不阻挡页面**；有通知时默认只显示
  铃铛，警告/错误自动展开；保留未读、全部已读、清空、逐条移除、动作按钮。新增键盘 Escape 收起并
  把焦点交还铃铛、读屏通知、窄窗口宽度钳制（240–380px）、诊断编号展示、导出诊断入口。
- **启动反馈**：移除顶部 `StartupWarningBar` InfoBar；启动警告、启动阻断/失败统一经
  `IDiagnosticService` 写本地脱敏日志并发布带诊断编号的通知（同一启动任务键更新，不刷屏）。
- **诊断**：`DiagnosticService` 是警告/可处理错误的唯一入口：生成 `FCD-yyyyMMdd-HHmmss-XXXX`
  编号、脱敏消息/堆栈、写 JSONL 滚动日志（单文件 1MB、最多 5 个归档）、发布通知；日志失败时
  保留通知并标注“本地日志写入失败”，不递归报错。`FileDiagnosticLog` 位于数据根 `Logs/`。
- **致命错误页**：`FatalErrorPage` 独立于数据库/导航；`App.UnhandledException` 记录致命诊断后
  打开独立窗口，提供诊断编号、脱敏详情、复制、导出、退出。Debug 故障注入
  `FURINA_UI7_SIMULATE_FATAL=1` 用于验收；普通构建不触发。
- **诊断上传**：本分支未发现既有服务端上传入口或已批准端点，标记 **Blocked**；本地报告文本/导出
  已实现（面板“导出诊断”、致命页“导出诊断报告”），上传预览/确认/取消/重试留待端点确定。

## 工作包 2：抽卡页完整迁移

- 刷新（手动链接、Windows 网页缓存、登录账号 SToken）：同一任务键更新进度与最终计数；失败只走
  诊断服务，不重复发失败通知；取消清除进度，不产生失败。
- 导入（UIGF、提瓦特 Helper、Portable）：目标/映射/冲突预览/覆盖确认保留；进度与最终计数通知；
  提交成功但列表重载失败显示“数据已提交，列表更新失败”+“重新加载”动作（只重查不重写）；
  切页后结果仍显示，页面不访问已离开控件。
- 导出（UIGF、表格、Portable）：保存对话框保留；进度通知；完成显示可读位置（无 `content://`），
  仅在文件存在时提供“打开文件夹”动作；取消不产生失败通知；失败诊断。
- 记录：纠正、普通删除、永久删除、撤销最近一次变更结果通知；确认框保留；不可逆完成后不提供再次
  提交动作；失败/事实变化/动作过期走诊断。
- 页面精简：移除常驻状态文本、底部数据库路径、导入/数据操作进度弹窗与结果弹窗；保留必要的范围/
  目标/账号/全量模式说明；输入校验改就地 `GachaInlineValidation`（无预留空白）。

## 验证

| 项 | 结果 |
|---|---|
| Release tests | 1004/1004 |
| WinUI Debug x64 构建 | 0 错误 |
| Release publish | `eng/Publish-WinUI.ps1` 成功 |
| 版权头 / `git diff --check` | Pass |
| Lead UIA 工作包 1 | 19 项 Pass（隐藏、警告+日志、已读/清空、键盘焦点、窄窗、启动失败降级、致命页） |
| Lead UIA 工作包 2 | 14 项 Pass（导入成功/失败、导出成功/取消、记录删除、撤销） |
| 诊断上传 | Blocked（无端点） |
| 刷新/冲突预览/切页完成/动作过期 | Not run（需菜单弹出层/受控网络/真实凭据；代码已迁移） |

证据：`artifacts/ui7-notification/lead-acceptance/{REPORT.md,Test-Ui7Notifications.ps1,Test-Ui7Gacha.ps1,uia7-*.json}`。

## 剩余

1. 工作包 3：完整 `.fca`、分类 Portable、档案与账号、操作历史、登录凭据、设置、领域采集的重复反馈清理。
2. 工作包 4：页面精简与普通构建复核（概览、Chronicle、角色武器、挑战等）。
3. 诊断上传：确定端点后补预览/确认/失败/重试验收；默认自动上传仍需维护者决策与用户授权。
4. 依赖菜单弹出层/受控网络/真实凭据的验收路径按 WP4/真实环境补测。
