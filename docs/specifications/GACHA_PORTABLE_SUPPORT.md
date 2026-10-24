# Gacha Portable 支持矩阵

状态：8B.4 实现状态；格式未公开发布

| 能力 | 版本/平台 | 当前状态 |
|---|---|---|
| Furina Gacha Portable 读取 | `1.x` | 支持；未知可选字段忽略，未知枚举受控拒绝 |
| Furina Gacha Portable 写出 | `1.0` | 支持；只接受已解析原神账号及满足 UIGF 必需字段的记录 |
| Future major | `2.x+` | 写数据库前拒绝，错误码 `UnsupportedVersion` |
| 旧 Furina Portable | 不存在 | 没有虚构 v0 Migrator |
| 标准载荷 | UIGF `v4.2` | 每账号独立且可单独读取 |
| 数据库导出 | SQLite Schema 5 | 单只读事务、游标分页、临时文件验证后替换 |
| 导入 Inspect/Validate/Migrate | Windows/Android 共用代码 | 已实现纯格式读取和校验 |
| 导入 Plan/Preview | Windows/Android 共用代码 | 已实现只读 AddOnly 规划，不修改数据库 |
| 生产 AddOnly Apply | SQLite Schema 5 | 服务入口已实现；档案、身份、账号、别名、事实、收据和 ChangeSet 同事务 |
| CSV/XLSX 可读导出 | Windows/Android 共用代码 | 包含所选范围、档案/身份、原始字段、来源和时间说明 |
| 产品文件导入／导出 UI | Windows/Android | picker、私有暂存、预览／映射／Apply、复验后 saver、分阶段进度和页面操作锁定已由维护者双端验证 |
| Windows 平台构建 | `net10.0-windows10.0.19041.0 / win-x64` | 2026-10-18 Release 独立输出构建通过，0 警告、0 错误 |
| Android 平台构建 | `net10.0-android` | 2026-10-18 Release 构建通过，SDK `E:\AndroidSDK`，0 警告、0 错误 |
| Windows ↔ Android 文件选择器实机往返 | — | 维护者报告双平台实际流程及 UI／快路径回归均可行；格式保持“未发布” |
| 真实数据库导入再导出 | 自动化＋双端维护者验收 | 导出后再导入幂等；多次进程中断后数据正常 |

当前输入源先以有限 buffer 复制到私有、可 seek 暂存并计算容器 SHA-256；产品导入随后使用一次会话专用 SQLite，增量关联 UIGF/supplement，分页生成 Plan/冲突，并以两遍分页读取在单一主数据库事务中 Apply。旧 `IGachaPortablePackageReader` 只保留为兼容 Codec／小型 fixture，不注册为产品 UI 依赖。SQLite 导出端和 ZIP 交付复验同样为分页／流式。

100k Reader＋Plan 已取得 Windows x64 宿主自动化证据。真实双平台样本为 9 个账号、14,484 条记录；空目标新账号在无任何 Tombstone／历史引用时使用受限版本 1 快路径，Windows x64 临时数据库两份样本端到端分别为 2,854 ms／2,781 ms，Apply 为 930 ms／936 ms。快路径之外仍保留完整版本和 Tombstone 检查。维护者随后确认该现实样本在 Windows 和 Android 上均可于数秒内完成导入，进度反馈与操作锁定正常，内存使用量无明显变化。当前没有可用的 100k 真实数据集，其设备级耗时、峰值内存、中断和低空间组合验证作为后续非阻塞压力测试保留。2,000,000 条限制仍是防御性安全上限，不是公开设备性能承诺；格式保持未发布。

## 结构化错误

`GachaPortableErrorCode` 当前覆盖：包无效、不支持版本/枚举/账号、必需字段缺失、重复或无效引用、无效路径、意外/缺失条目、长度/哈希/记录数/记录内容不一致、资源超限和输入不可寻址。

错误包含可用时的载荷路径和记录 ID；不得包含 Cookie、SToken、AuthKey 或其他秘密。
