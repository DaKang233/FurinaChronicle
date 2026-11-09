# Phase 8C 实施与交接

里程碑为 **0.8.16 / 8C.7**。业务数据库为 Schema 12，内部 Archive 容器为 1.0。当前验收结论见[退出评审](PHASE_8C_EXIT_REVIEW.md)。

| 工作包 | 交付 | 实施记录 |
|---|---|---|
| 8C.0 | 确定逻辑保真范围、排除项和落库边界 | [保真矩阵](../specifications/ARCHIVE_V1_FIDELITY_MATRIX.md)、[ADR 0021](../decisions/0021-logical-archive-v1-fidelity.md) |
| 8C.1 | 一致快照导出、读取校验、资源限制与版本派发 | [8C.1](PHASE_8C1_IMPLEMENTATION.md) |
| 8C.2 | 可审查 RestorePlan、显式映射、冲突与确认 | [8C.2](PHASE_8C2_IMPLEMENTATION.md) |
| 8C.3 | 跨领域原子 Merge、目标本地 Receipt、权威设置 | [8C.3](PHASE_8C3_IMPLEMENTATION.md) |
| 8C.4 | 分层 Scoped Replace、恢复点、旧 Undo 失效 | [8C.4](PHASE_8C4_IMPLEMENTATION.md) |
| 8C.5 | 双端备份与恢复页、分类 Portable、Markdown 出口 | [8C.5](PHASE_8C5_IMPLEMENTATION.md) |
| 8C.6 | 双向真机往返、严格比较、保真修复及 Schema 11 | [8C.6](PHASE_8C6_IMPLEMENTATION.md)、[修复记录](PHASE_8C6_FIXES_20261029.md) |
| 8C.7 | 独立进程终止探针、设备证据索引、迁移与 Phase 9 交接 | [退出评审](PHASE_8C_EXIT_REVIEW.md)、[探针操作](../../eng/phase8c/README.md) |

## 支持矩阵

| 入口 | 使用范围 | 保真与恢复边界 |
|---|---|---|
| 完整逻辑 Archive 1.0 | Windows、Android 内部文件迁移。全选或选择导出范围 | 保留已支持事实、来源、观察、已保留修订、空档案与账号和声明的设置。目标生成新的 Receipt 与本机操作记录 |
| Archive Merge | Preview、确认、原子补充事实 | 显式处理映射及冲突。同一 OperationId 与原包和 Plan 重放返回原 Receipt |
| Archive Scoped Replace | Archive／Account／Domain 明确且不重叠的范围 | 精确替换已确认范围，按契约失效旧 Undo。提交前生成本机一致恢复点 |
| UIGF 4.2 | 抽卡社区标准交换 | 只表达标准及兼容扩展所覆盖内容。Archive 以独立历史载荷保留其余可表达的逻辑事实 |
| 分类 Portable | Gacha、角色与武器、挑战的选择性迁移 | 各领域按已有规范保留自身语义。格式为 internal / unpublished |
| Markdown | 查看和归档 | 人类可读导出 |

Archive 1.0 的范围、兼容性和限制以[内部格式规范](../specifications/ARCHIVE_V1_PROPOSED.md)和[保真矩阵](../specifications/ARCHIVE_V1_FIDELITY_MATRIX.md)为准。源凭据、设备设置、Undo 栈、目标本地 Receipt、Tombstone 与 Chronicle 投影均排除在跨端载荷之外。目标端的活动 Tombstone 仍参与受控确认。

## 迁移与失败恢复

- 更新前保留 SQLite 一致备份。支持的已知 Schema 由客户端逐步升级，Schema 10→11 在事务内增加未解析账号原因。已经丢失的来源原因无法由迁移推断。
- Schema 11→12 在事务内重建两张 Archive Receipt 映射表。目标档案删除后仍保留原历史 ID 和 Receipt。迁移还新增 Gacha 批次和 Portable 操作摘要列。旧结果没有可证实的计数时保持未知。
- 检测到未来 Schema、错误的 application ID 或无法可靠识别的数据库时，保留原库并拒绝写入。Schema 12 升级成功后没有数据库降级流程。
- 计划与目标状态令牌绑定。遇到目标变动、失败或中断后，重新打开并核对持久化结果。没有已提交结果时重新 Preview。已提交但界面结果丢失时，凭原 OperationId 查本机 Receipt。
- Scoped Replace 提交前的进程中断由 SQLite 回滚，启动清理移除未引用恢复点。提交后持久化结果和已引用恢复点继续有效。恢复点当前可校验，主库回切属于单独工作。

## Phase 9 交接

以 Schema 12、共享身份和来源模型为基线，先定义角色解析、认证和采集的小能力接口。首个采集纵切优先角色／武器状态和挑战，补齐刷新批次、限流、重试与原始证据保留。Provider 的具体来源与持久化范围按真实数据语义逐一冻结。8C 退出结论见[退出评审](PHASE_8C_EXIT_REVIEW.md)。UI 路线按独立门槛推进。
