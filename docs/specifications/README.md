# 格式规范

此目录将保存 Furina Archive、Portable Dataset、JSON Schema 和示例。

当前架构方向已经接受，但正式字段尚未冻结。8B.0 只冻结会影响数据解释、跨端身份、公开兼容或迁移安全的最小共享契约；首个数据集在 8B.2 实施前建立格式说明、Schema、最小和完整示例、版本迁移规则，发布时完成 Windows/Android 往返验证。完整 Archive 在 8C 实施前冻结，不要求所有领域 Schema 同时完成。

在这些文件完成前，不得对外宣称 Furina 私有格式已经稳定。

## 产品规范与子系统草案

- [Chronicle 产品需求](CHRONICLE_PRODUCT_REQUIREMENTS.md)：已接受的主时间线、角色/武器/挑战时间线、显示模式、手工获取和验收行为。
- [Chronicle Timeline v1](CHRONICLE_TIMELINE_V1.md)：Chronicle 子系统的投影身份、排序和贡献者草案；共享基础以各自规范为准，字段在 8B.6 实施前冻结，更早进入持久化/公开格式的部分提前冻结。

领域独立的标准、Furina 机器可读载荷和人类可读出口见 [Portable/Export](../architecture/PORTABLE_DATASET.md)。规范化事实不依赖 Chronicle 草案字段，也不以 Contributor 为成立条件。
