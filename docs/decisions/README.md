# 架构决策记录

本目录只记录会影响产品边界、数据语义、公开格式、安全或长期兼容性的决定。项目由单人主维护，不要求普通内部重构撰写 ADR。

状态包括 `Proposed`、`Accepted`、`Superseded` 和 `Rejected`。新增 ADR 应包含背景、选项、决定、原因、后果、迁移影响和测试要求。

- [0001：本地优先的个人历史数据库](0001-local-first-history-database.md)
- [0002：档案内账号副本与共享角色身份](0002-archive-account-copy-identity.md)
- [0003：Portable Model 独立于 SQLite](0003-portable-model.md)
- [0004：分类导入、修订和有限撤销](0004-import-revision-undo.md)
- [0005：可选服务端能力分离](0005-optional-server-capabilities.md)
- [0006：档案名称和唯一性策略](0006-archive-name-policy.md)
- [0007：不可逆本地删除](0007-local-irreversible-deletion.md)
- [0008：离线优先的卡池事件元数据](0008-offline-gacha-event-metadata.md)
- [0009：Chronicle 作为派生投影](0009-chronicle-as-derived-projection.md)
