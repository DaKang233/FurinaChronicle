# 格式规范

此目录保存 Furina Archive、Portable Dataset、JSON Schema 和示例。

当前架构方向已经接受。首个实际领域格式 [Furina Gacha Portable v1](GACHA_PORTABLE_V1.md) 已冻结为 Accepted、未发布，并提供 [支持矩阵](GACHA_PORTABLE_SUPPORT.md)、`schemas/` 下的机器校验规则及 `examples/` 下的最小/完整语义示例。公开发布仍需完成 Windows/Android 实际文件往返。完整 Archive 已形成 [保真矩阵](ARCHIVE_V1_FIDELITY_MATRIX.md)、[内部实施规范](ARCHIVE_V1_PROPOSED.md)、[恢复规划规范](ARCHIVE_RESTORE_PLAN_V1.md)、[原子 Merge 规范](ARCHIVE_ATOMIC_MERGE_V1.md)和[契约示例](examples/ARCHIVE_V1_PROPOSED_EXAMPLES.md)；8C.1～8C.3 已完成导出、规划和内部 Merge，但尚未公开发布。

未完成对应格式的发布门槛前，不得对外宣称 Furina 私有格式已经稳定发布。

## 产品规范与子系统草案

- [Chronicle 产品需求](CHRONICLE_PRODUCT_REQUIREMENTS.md)：已接受的主时间线、角色/武器/挑战时间线、显示模式、手工获取和验收行为。
- [Chronicle Timeline v1](CHRONICLE_TIMELINE_V1.md)：Chronicle 子系统的投影身份、排序和贡献者草案；共享基础以各自规范为准，字段在 8B.6 实施前冻结，更早进入持久化/公开格式的部分提前冻结。
- [角色／武器历史与 Portable v1](CHARACTER_WEAPON_HISTORY_V1.md)：8B.5 已实现的内部格式，携带不可变状态观察、手工事实及修订链；尚未公开发布。
- [挑战观察历史与 Portable v1](CHALLENGE_HISTORY_V1.md)：8B.5 已实现的内部格式，首批仅支持深境螺旋；尚未公开发布。
- [8B.5 领域支持矩阵](ARCHIVE_DOMAIN_HISTORY_SUPPORT.md)：记录实际输入、查询、导出、版本、平台和未实施能力。
- [Archive v1 保真矩阵](ARCHIVE_V1_FIDELITY_MATRIX.md)：覆盖 Schema 7 的 22 表基线、Schema 8 的恢复本地状态增量及非表状态，区分逻辑事实、本机运行状态和可重建缓存。
- [Archive v1 内部实施规范](ARCHIVE_V1_PROPOSED.md)：8C.0／8C.1 的容器、版本、资源限制和状态机契约；当前未发布。
- [Archive RestorePlan v1](ARCHIVE_RESTORE_PLAN_V1.md)：8C.2 的映射、冲突、确认、资源和新鲜度契约。
- [Archive Atomic Merge v1](ARCHIVE_ATOMIC_MERGE_V1.md)：8C.3 的单事务、Receipt、Undo、Tombstone、取消与 Chronicle 失效契约。

领域独立的标准、Furina 机器可读载荷和人类可读出口见 [Portable/Export](../architecture/PORTABLE_DATASET.md)。规范化事实不依赖 Chronicle 草案字段，也不以 Contributor 为成立条件。
