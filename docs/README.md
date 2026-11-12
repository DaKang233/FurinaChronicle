# FurinaChronicle 文档索引

本目录是 FurinaChronicle 产品边界、数据语义和开发约束的规范来源。

## 项目

- [项目宪章](project/PROJECT_CHARTER.md)
- [范围与非目标](project/SCOPE_AND_NON_GOALS.md)
- [路线图](project/ROADMAP.md)
- [Phase 8A 退出评审](project/PHASE_8A_EXIT_REVIEW.md)
- [Phase 8B 实施计划](project/PHASE_8B_PLAN.md)
- [Phase 8B.7 实施与设备验收状态](project/PHASE_8B7_IMPLEMENTATION.md)
- [Phase 8B 退出评审](project/PHASE_8B_EXIT_REVIEW.md)
- [Phase 8C 输入清单](project/PHASE_8C_INPUTS.md)
- [Phase 8C 实施计划](project/PHASE_8C_PLAN.md)
- [Phase 8C 实施与交接](project/PHASE_8C_IMPLEMENTATION.md)
- [Phase 8C 退出评审](project/PHASE_8C_EXIT_REVIEW.md)
- [UI 重构实施记录（UI-3/UI-4/UI-5）](project/UI_REDESIGN_UI3_IMPLEMENTATION.md)
- [UI-4 管理闭环实施记录](project/UI_REDESIGN_UI4_IMPLEMENTATION.md)
- [UI-5 领域页实施记录](project/UI_REDESIGN_UI5_IMPLEMENTATION.md)
- [UI-6 生产切换评审](project/UI6_PRODUCTION_SWITCH_REVIEW.md)
- [WinUI 发布与默认入口](project/UI6_RELEASE_PACKAGING.md)
- [UI-6 无障碍与性能记录](project/UI6_ACCESSIBILITY_PERFORMANCE.md)
- [UI-6 双端收口实施记录](project/UI6_IMPLEMENTATION.md)
- [Phase 8C.5 产品流程实施记录](project/PHASE_8C5_IMPLEMENTATION.md)
- [Phase 8C.6 双向往返验收规范](specifications/PHASE_8C6_ROUNDTRIP_ACCEPTANCE.md)
- [Phase 8C.6 工程实施与设备验收状态](project/PHASE_8C6_IMPLEMENTATION.md)
- [Phase 8C.6 验收缺陷修复记录](project/PHASE_8C6_FIXES_20261029.md)
- [Phase 8C.6 DSH / UIA 补测指南](development/PHASE_8C6_RETEST_GUIDE.md)
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
- [Chronicle 时间线](architecture/CHRONICLE_TIMELINE.md)
- [页面选择状态与跨页刷新](architecture/PRESENTATION_SELECTION_STATE.md)

## 决策与开发

- [决策记录](decisions/DECISION_REGISTER.md)
- [ADR 索引](decisions/README.md)
- [测试要求](development/TESTING.md)
- [持续集成与发布构建](development/CONTINUOUS_INTEGRATION.md)
- [Android 真机构建与部署](development/ANDROID_PHYSICAL_DEVICE_DEPLOYMENT.md)
- [版本策略](development/VERSIONING.md)
- [版权头规范](development/COPYRIGHT_HEADERS.md)
- [Chronicle 产品需求](specifications/CHRONICLE_PRODUCT_REQUIREMENTS.md)
- [Chronicle Timeline v1 草案](specifications/CHRONICLE_TIMELINE_V1.md)

## 文档状态

- `Accepted`：当前实现和新增代码必须遵守。
- `Proposed`：推荐设计，尚未完成最终人工确认。
- `Target`：已接受的目标架构，但代码迁移可能尚未完成。
- `Accepted target`：产品或架构方向已经接受，但对应代码、迁移或字段冻结尚未全部完成。
- `Historical`：仅用于说明历史，不再指导新增实现。

文档描述目标模型时必须同时说明当前代码是否已经实现，避免 Agent 将规划误认为现状。
