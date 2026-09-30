# FurinaChronicle 文档索引

本目录是 FurinaChronicle 产品边界、数据语义和开发约束的规范来源。

## 项目

- [项目宪章](project/PROJECT_CHARTER.md)
- [范围与非目标](project/SCOPE_AND_NON_GOALS.md)
- [路线图](project/ROADMAP.md)
- [术语表](project/GLOSSARY.md)
- [待确认事项](project/OPEN_QUESTIONS.md)

## 原则

- [数据原则](principles/DATA_PRINCIPLES.md)
- [兼容策略](principles/COMPATIBILITY_POLICY.md)

## 架构

- [架构总览](architecture/OVERVIEW.md)
- [身份模型](architecture/IDENTITY_MODEL.md)
- [时间与来源](architecture/TIME_AND_PROVENANCE.md)
- [数据采集](architecture/DATA_ACQUISITION.md)
- [修订、操作历史与撤销](architecture/REVISION_AND_UNDO.md)
- [导入与合并](architecture/IMPORT_AND_MERGE.md)
- [可移植数据集](architecture/PORTABLE_DATASET.md)
- [备份与恢复](architecture/BACKUP_AND_RESTORE.md)
- [服务端扩展](architecture/SERVER_EXTENSIBILITY.md)

## 决策与开发

- [决策记录](decisions/DECISION_REGISTER.md)
- [ADR 索引](decisions/README.md)
- [测试要求](development/TESTING.md)
- [版权头规范](development/COPYRIGHT_HEADERS.md)

## 文档状态

- `Accepted`：当前实现和新增代码必须遵守。
- `Proposed`：推荐设计，尚未完成最终人工确认。
- `Target`：已接受的目标架构，但代码迁移可能尚未完成。
- `Historical`：仅用于说明历史，不再指导新增实现。

文档描述目标模型时必须同时说明当前代码是否已经实现，避免 Agent 将规划误认为现状。
