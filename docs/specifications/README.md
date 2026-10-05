# 格式规范

此目录保存 Furina Archive、Portable Dataset、JSON Schema 和示例。

当前架构方向已经接受。首个实际领域格式 [Furina Gacha Portable v1](GACHA_PORTABLE_V1.md) 已冻结为 Accepted、未发布，并提供 [支持矩阵](GACHA_PORTABLE_SUPPORT.md)、`schemas/` 下的机器校验规则及 `examples/` 下的最小/完整语义示例。公开发布仍需完成 Windows/Android 实际文件往返。完整 Archive 在 8C 实施前冻结，不要求所有领域 Schema 同时完成。

未完成对应格式的发布门槛前，不得对外宣称 Furina 私有格式已经稳定发布。

## 产品规范与子系统草案

- [Chronicle 产品需求](CHRONICLE_PRODUCT_REQUIREMENTS.md)：已接受的主时间线、角色/武器/挑战时间线、显示模式、手工获取和验收行为。
- [Chronicle Timeline v1](CHRONICLE_TIMELINE_V1.md)：Chronicle 子系统的投影身份、排序和贡献者草案；共享基础以各自规范为准，字段在 8B.6 实施前冻结，更早进入持久化/公开格式的部分提前冻结。

领域独立的标准、Furina 机器可读载荷和人类可读出口见 [Portable/Export](../architecture/PORTABLE_DATASET.md)。规范化事实不依赖 Chronicle 草案字段，也不以 Contributor 为成立条件。
