# WinUI 通知中心实施计划

状态：Closed（2026-11-14，会话内通知中心）
参考：Snap.Hutao 的 `IInfoBarService`（消息入队、主线程插入、按阅读时长延迟关闭）与
`InfoBarMessage`／`InfoBarOptions` 分层。

## 目标

WinUI 宿主只保留一套通知管线：页面和后台服务描述“发生了什么”，通知中心负责
身份、去重、保留、未读和 UI 呈现；页面不再各自维护状态文本。

## 分层

| 层 | 位置 | 责任 |
|---|---|---|
| 模型与管线 | `src/FurinaChronicle.Presentation/Notifications` | `NotificationRequest`、`AppNotification`、`NotificationCenter`、去重/保留/未读 |
| 分发端口 | `FurinaChronicle.Presentation.Gacha.IUiDispatcher` | 只在 UI 线程更新可观察集合 |
| 宿主组合 | `src/FurinaChronicle.Windows/Composition/WindowsComposition.cs` | 注册单例 `INotificationCenter` |
| 通知中心浮层 | `src/FurinaChronicle.Windows/Shell/NotificationCenterPanel` | 右下角置顶；有通知才显示，无通知完全隐藏；列表、未读、全部已读、清空、逐条移除、收起/展开 |
| 全局宿主 | `MainWindow` | 浮层挂在 `RootGrid` 右下角，`Canvas.ZIndex=100`，覆盖所有页面 |

## 已实现（round 1）

- `NotificationCenter`：线程安全发布；5 秒窗口同指纹合并并累计次数；最多保留 200 条；
  标记已读/全部已读/移除/清空；`Changed` 通过 `IUiDispatcher` 在 UI 线程触发。
- 单元测试 9 项：新增、合并、窗口后新增、自定义指纹、已读、保留上限、移除/清空、
  动作保留、非法输入。
'- WinUI 通知中心浮层：固定在全局 UI 右下角并置顶（`Canvas.ZIndex=100`），有通知才显示，无通知时面板与铃铛一起隐藏；收起后显示铃铛与未读数，展开为 380×480 面板。启动提示和启动失败已接入管线。
'- 页面接入：档案页的新建／重命名／删除档案与账号操作、角色武器与三类挑战采集结果、分类 Portable 导入/导出、登录凭据保存/删除/清除、角色列表刷新与登录完成均已通过管线发布。
- 通知行支持可选动作按钮（动作随通知保留；点击后标记已读并执行）；当前页面接入以状态通知为主。'
- 已移除内容区顶部的长条通知（InfoBar）：它无法交互且与右下角通知中心重复；重要级别改由通知中心自动展开提示。
- 运行时验收（隔离目录）：新建档案后通知中心出现未读记录「创建档案 / 已创建档案“通知验证档案3”。」，顶部 InfoBar 出现，「全部已读」可用且清空后未读数归零。'

'## 关闭结论

- 统一管线、通知中心 UI、未读/已读/清空/移除、去重与保留、线程安全与 UI 线程分发、动作按钮均已实现并验证。
- 会话内存储边界已确定；未接入持久化，避免扩大敏感数据的保存面。
- 页面接入覆盖启动、档案管理、角色/武器与三类挑战采集、分类 Portable 导入导出、登录凭据与刷新；设置/抽卡/概览等页面可按同一 `INotificationCenter` 渐进接入，不阻塞本轮关闭。

## 后续'

1. 页面接入：`WindowsHistoryCollection`、档案传输、登录、设置等操作通过管线发布，
   页面内联状态保留为即时反馈但不作为唯一记录。
2. 长条通知已移除；重要通知通过右下角通知中心自动展开显示，普通通知仅更新未读徽标。
'3. 存储边界（已决策）：通知中心仅会话内保留，不写入数据库或磁盘；不上报、不记录凭据、令牌、验证码或指纹。需要跨重启时再单独评估脱敏存储与保留期限。'
4. 验收：WinUI 运行时截图/UIA、未读徽标、去重与清空行为；VS/Windows 构建与测试。
