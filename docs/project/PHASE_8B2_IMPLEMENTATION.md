# Phase 8B.2 实施契约

状态：Accepted implementation baseline

确认日期：2026-10-06

基线：`feat/phase-8`，起始提交 `8a3ac23`

## 目标

为现有 Genshin Gacha 提供首个真实 Portable / Export 基础，使标准事实和 Furina 补充语义能够脱离 UI、SQLite 和 Chronicle 被带走、校验、解释并生成只读导入计划。

首版采用 [Furina Gacha Portable v1](../specifications/GACHA_PORTABLE_V1.md) 和 [ADR 0012](../decisions/0012-gacha-portable-v1.md)。该格式已接受但尚未公开发布；在完成跨平台文件往返和发布检查前不得宣称为稳定发布格式。

## 工作包

### P0：契约

- 冻结单源档案、多已解析账号副本、UIGF 4.2＋补充 NDJSON 的 ZIP 结构；
- 冻结关联键、来源解释、支持范围、资源限制和 structured error；
- 明确生产 Apply 依赖 8B.3/8B.4 的 DataChangeSet 事务基础。

### P1：纯 Portable Model 与 Codec

- Services 只包含平台无关的 Portable Model、校验结果、只读计划和小接口；
- Infrastructure 实现 ZIP、JSON、NDJSON、UIGF 4.2 互操作视图、版本分派和校验；
- 使用 Stream 与 CancellationToken，不创建 SQLite，不依赖 MAUI 或 Chronicle；
- 未知 future major、未知枚举、路径、哈希、长度、关联、截断和超限均返回结构化错误。

### P2：一致导出

- 从同一次 SQLite 一致读取快照生成标准和补充载荷；
- 不使用 `Count + OFFSET` 跨多个可变化查询构造包；
- 流式写入临时目标并验证成功后交付；失败或取消不覆盖已有成功文件；
- 缺失可选元数据不联网补齐，缺失必需字段按支持范围拒绝。

### P3：只读 Plan/Preview

- `Inspect / Validate / Migrate / Plan / Preview` 不创建或修改档案、账号、身份别名和记录；
- 档案名称精确匹配保留首尾空白，模糊匹配只产生候选；
- 账号按规范自然身份和共享 GUID 规则映射，包内副本不因共享身份合并；
- v1 只提供 AddOnly 计划，明确新增、保持、冲突、无法迁移、身份别名和接收上下文。

### P4：人类可读导出

- 复用 CSV/XLSX，至少一种输出包含源档案、账号范围、规范身份、记录 ID、保存时间和偏移、来源、抓取/导入时间、生成时间与未知说明；
- 无 Chronicle、无网络、无元数据缓存时仍可查看；
- 允许展示机器 Portable v1 暂不支持的旧记录，并明确缺失字段。

### P5：兼容与交接

- 提供 Schema、最小/完整示例、支持矩阵和结构化错误说明；
- 完成纯格式往返、身份/关联/时间/来源/版本/大数据/一致性 fixture；
- 分开记录 Windows/Android 编译、文件读写、纯格式往返和真实数据库往返证据。

## 明确不进入本轮

生产 Apply、真实别名持久化、Revision/Undo、完整 Archive、Chronicle、ChangeJournal、Sync、云服务、任意领域插件注册中心、其他游戏和新领域 Schema。

内存执行器只能验证规则，不得注册为生产 Apply 替代品。现有 `ImportUigfGachaRecords` 的逐账号提交不能作为 Portable 包的原子写入边界。

## 退出状态定义

8B.2 关闭时分别报告：

1. 格式/导出/纯模型往返；
2. 只读导入计划/预览；
3. 生产 Apply/DataChangeSet/真实库重导入仍待 8B.3/8B.4；
4. 格式处于 Accepted 未发布或已发布；
5. Windows/Android 各层验证证据。

自动测试不替代平台文件选择器和真实设备文件往返。所有 fixture 使用虚构 UID、GUID 和记录，不包含真实凭据、AuthKey、Cookie 或 Token。
