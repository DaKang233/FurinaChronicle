# Archive 双端产品流程 v1

状态：Accepted internal implementation contract；未公开发布

适用阶段：Phase 8C.5

依赖：ADR 0021～0023、Archive v1、Archive Restore Plan v1、
Archive Atomic Merge v1、Archive Scoped Replace v1

## 目的

本流程把已经实现的逻辑 Archive 导出、验证、Merge 和 Scoped Replace 接入现有
Windows／Android MAUI 宿主。它不改变 Archive 数据语义，不建立第二套写入路径，也不把
内部 Archive 或领域 Portable 自动声明为公开兼容格式。

## 正式入口与格式说明

- Windows 在现有侧栏提供“备份与恢复”入口。
- Android 从“档案与账号管理”进入完整的备份／恢复页面，不增加新的底部页签。
- “完整逻辑 Archive”可由 FurinaChronicle 重新导入，保留 Archive v1 保真矩阵声明的
  当前事实、来源、历史观察、已保留 Revision、空父对象和白名单设置。
- “分类 Portable”只覆盖所选领域，并继续标记为 internal / unpublished。
- UIGF 是抽卡社区标准；其导入／导出继续位于抽卡页，并受 UIGF 可表达字段约束。
- Markdown 是只读、人类可读报告，不能重新导入，也不能替代逻辑 Archive、领域
  Portable 或 UIGF。

任何尚无正式实现的按钮必须隐藏或禁用并说明原因，不能以占位点击伪装可用。

## 导出

用户可选择全部档案或一个档案。正式导出通过 `IArchiveLogicalExporter` 取得一致逻辑
快照，并通过系统文件保存边界写出：

- Windows 使用系统文件保存选择器；
- Android 使用系统文档提供器返回的流；URI 不解释为普通文件路径，也不写入 Archive。

导出先在应用私有缓存中生成临时文件，系统保存成功、取消或失败后均清理该临时文件。
文件名扩展为 `.fca`；该扩展只是当前内部容器标识，不构成公开格式承诺。

## 恢复预览

系统文件选择器只提供输入流。`IArchiveRestoreInputStager` 在应用私有目录完成复制、哈希、
Reader／Validator 校验与索引；页面不得直接持有平台路径或 Android URI 作为恢复依据。

恢复按以下顺序进行：

1. 选择文件并完成 staging；
2. 选择 Merge 或 Replace；
3. 对档案名称歧义、Unresolved 账号和 Replace 范围作显式决定；
4. 显示档案／账号映射、各领域 Add／Keep／Replace／Remove、冲突、Tombstone、
   设置差异、Undo 资格及空间估算；
5. 用户确认所有未满足警告；
6. 仅当 `ArchiveRestorePlan.CanApply` 为 true 时启用提交。

Merge 与 Replace 都复用同一 `IArchiveRestorePlanner` 和 `IArchiveAtomicRestorer`。
UI 不直接读写 SQLite，也不按领域依次 Apply。

## 会话、取消与进程终止

- 页面退出、用户取消或选择另一个文件时释放当前 staged package；再次进入必须重新
  选择并预览。
- 应用进程终止后不恢复内存中的 Plan、确认或平台文件权限；残留私有 staging 只可作为
  待清理临时文件，不能直接继续 Apply。
- 开始数据库 mutation 前允许取消；进入原子提交后等待提交或回滚结果，不能把“取消已
  请求”描述为“没有提交”。
- OperationId 在一次预览会话内稳定。Apply 返回不确定或提交后 UI 刷新失败时，页面先
  通过 `FindReceiptAsync` 查询持久化 Receipt，再决定显示“已提交”或“结果未知”；
  不依赖内存布尔值，不自动使用新 OperationId 重复提交。

## 结果、刷新与恢复点

- Applied、NoOp 和 AlreadyCommitted 都显示持久化 Receipt 摘要。
- 业务提交成功后，页面或其他业务页刷新失败不得描述为恢复失败，也不得回滚已提交数据；
  文案应提示用户重新进入业务页或按 Receipt 重试显示。
- 恢复导致档案／账号新建、删除或重映射后，各业务页重新进入时按自己的稳定 ID 校验并
  修复选择；备份页不写其他页面的选择状态。
- Replace 前设备本地恢复点只显示标识、时间、大小和校验状态。8C.5 不提供“回切主库”
  按钮；该能力必须先有独立的关闭连接、WAL／SHM 和原子文件切换契约。

## Gacha Markdown

Gacha Markdown 分为摘要和独立明细：

- 摘要说明档案、账号范围、记录数、时间范围、卡池／星级计数、数据来源分布及完整性
  声明；
- 明细逐条保存账号、记录 ID、发生时间与原始偏移、物品、星级、类型、卡池、数量、
  来源、观察／抓取／导入时间和有限来源引用；
- 缺失字段显示“未知”，不得用导出时间或元数据推断值冒充来源事实；
- 报告不依赖在线图片，不从 Chronicle 投影反推领域事实。

## 8C.5 自动化边界

必须验证：

- 正式入口只调用共享服务；
- 导出临时文件在成功、取消和失败路径均可清理；
- Markdown 保留来源、偏移、未知字段和不完整声明；
- 未解决映射、冲突或确认项存在时不能 Apply；
- Receipt 查询能够区分已提交与结果未知；
- 现有 UIGF、领域 Portable、Merge／Replace 和统一全量模式回归通过。

Windows／Android 系统选择器、后台／页面退出及真机文件流程属于设备证据。未运行时明确
记录为 `Not run` 或 `Awaiting device evidence`，不得由自动化或构建结果替代。
