# 兼容策略

状态：Accepted

## 独立版本

以下版本独立演进：

- `ArchiveFormatVersion`
- `DatasetFormatVersion`
- `StandardCodecVersion`
- `DatabaseSchemaVersion`
- `AppVersion`

不得使用应用版本代替数据格式版本。

## 版本规则

- major 增加表示可能不兼容；不支持的未来 major 默认拒绝导入。
- minor 增加必须保持同一 major 内向后兼容。
- 增加可选字段通常只提升 minor。
- 删除字段、改变字段含义或改变身份规则必须提升 major。
- 未知可选字段应保留或安全忽略；未知枚举不得导致整个文件崩溃。
- 从旧版本迁移必须通过显式、确定性的 Migrator，不采用“能反序列化多少算多少”。

## 标准格式

- 默认只导出当前正式支持的最新标准版本。
- 导入可以支持明确列出的旧版本。
- 每个标准的支持矩阵必须写明游戏、导入版本和导出版本。
- 标准 DTO 与内部领域模型分离，格式特有时间和字段语义只存在于 Codec 边界。

## 发布要求

改变可移植格式时必须同时提供：

- 更新后的格式说明；
- JSON Schema 或等价机器校验规则；
- 迁移器；
- 最小示例和完整示例；
- 旧版本导入测试；
- Windows 与 Android 往返测试。
