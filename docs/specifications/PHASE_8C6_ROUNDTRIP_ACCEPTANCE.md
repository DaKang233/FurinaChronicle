# Phase 8C.6 双向 Archive 往返验收规范

状态：Accepted implementation contract

日期：2026-10-25

## 目的

本规范验证已经接受的 Archive v1、Merge、Scoped Replace 与正式文件流程，不新增公开
格式或恢复语义。8C.6 必须分别给出自动化、Windows 运行、Android 真机和真实跨端文件
证据；构建通过、同一宿主临时数据库测试或同一文件反复复制都不能替代设备证据。

验收构建必须使用独立应用标识和独立数据库目录。它可以提供合成数据准备与证据摘要，
但导出、选择文件、Preview、确认、Apply、重新打开和再次导出必须复用 8C.5 正式服务及
页面，不得直连主数据库、凭据或通过验收专用 SQL 绕过正式恢复路径。

## 代表性合成档案

受版本控制的 fixture 定义至少覆盖：

- 两个以上档案，以及同一自然角色在不同档案中的独立账号副本；
- 空档案、空账号和 `Unresolved` 账号；
- Gacha 当前事实与 Revision；
- 手工获取的不同时间精度、原始偏移和 Revision；
- 同一物品 `A → B → A` 的多次观察；
- 完成时间未知的挑战观察；
- 官方、标准导入、用户输入等来源和声明的完整度；
- 目标端已有事实与活动 `LocalOnlyTombstone`。

源 Tombstone、Undo、ChangeSet、Receipt、Chronicle、凭据和设备设置不属于 Archive
内容。目标 Tombstone 只通过 Preview 中绑定 marker/version 的确认参与受控测试。

## 严格语义比较

比较器比较逻辑 Archive，不比较 ZIP 字节或 SHA-256。默认只允许忽略：

- 目标本地 `PlayerArchive.Id`、`GameAccount.Id` 和包内引用值；
- 目标本地 ChangeSet、并发版本、Undo、Receipt、Chronicle generation；
- Manifest 导出时间、生成平台、应用构建和 Commit；
- ZIP 条目顺序、压缩结果及容器哈希。

以下内容必须一致：

- 档案与账号的数量、精确名称、父子归属和空父对象；
- 已解析自然身份、共享身份 GUID，以及 `Unresolved` 字段与原因；
- 当前事实、稳定领域标识和已保留 Revision；
- 原始 `DateTimeOffset`、时间精度、不确定区间和未知时间；
- 来源、来源引用、抓取／导入／观察时间及完整度；
- `RequireUniqueArchiveNames` 与每档案 `UndoLimit`。

同名档案或同一自然身份的多份副本按多重集合比较，不能因名称或 UID 相同而合并。比较
失败必须报告所属档案、账号和事实类别；“文件哈希不同”不是语义失败原因。

比较器至少具备四个真实变异反例：删 Revision、改变时间精度、合并两个档案中的同 UID
副本、删除空档案。四者均必须失败；只改变允许忽略字段必须通过。

## 双向文件流程

Windows 发起：

1. 在 Windows 隔离库生成代表性 fixture 并通过正式入口导出 Archive；
2. Android 通过系统文件提供器选择、Preview、确认 Merge，重启后再次导出；
3. Windows 读取返回文件并执行严格语义比较。

Android 发起：

1. 在 Android 隔离库独立生成 fixture 并导出新的 Archive；
2. Windows 选择、Preview、确认 Merge，重启后再次导出；
3. Android 读取返回文件并执行严格语义比较。

两条链路不得共用同一个起始 Archive。每个平台还须验证选择器取消、Preview 后拒绝、
成功 Apply、重开后 Receipt/状态可见以及重复导入 NoOp。Replace 只选择一个明确
Archive、Account 或 Domain 范围，并保存该范围、确认项、恢复点和结果证据。

## 质量测量

每个方向至少记录：

- Archive 容器字节数与 Manifest 声明的未压缩字节数；
- staging 文件与临时目录峰值；
- Preview、Apply、重开、导出和严格比较耗时；
- 操作前基线、过程峰值和稳定后实际进程内存；
- 平台、系统版本、应用完整版本、Phase、Commit、Schema 和文件指纹。

宿主 100k 合成数据只作为算法与资源回归，不冒充手机性能。敏感路径、凭据、AuthKey、
Cookie、SToken 和私人 UID 不得进入证据文件、日志或提交。

## 退出状态

- 自动化与适用构建完成，但缺少实际设备交互：`Awaiting device evidence`；
- 单向完成或仅使用同一来源文件：`Partial device evidence`；
- 两个方向、成功／NoOp／取消、受控 Replace、重开和严格比较全部有真实证据：
  `Device workflow verified`。

主库恢复点回切仍未授权。8C.6 不实现热替换生产 SQLite、云同步或新的公开格式。
