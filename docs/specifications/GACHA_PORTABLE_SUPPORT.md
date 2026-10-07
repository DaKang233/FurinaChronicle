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
| 产品文件导入／导出 UI | Windows/Android | 已实现 picker、私有暂存、预览／映射／Apply、复验后 saver；真实系统 provider 待验收 |
| Windows 平台构建 | `net10.0-windows10.0.19041.0 / win-x64` | 2026-10-08 Release 构建通过，0 警告、0 错误；应用启动通过 |
| Android 平台构建 | `net10.0-android` | 2026-10-08 Release 构建通过，SDK `E:\AndroidSDK`；ADB 当前无设备 |
| Windows ↔ Android 文件选择器实机往返 | — | 尚未验收，格式保持“未发布” |
| 真实数据库导入再导出 | 自动化 fixture | 数据库 A→数据库 B→再导出语义通过；尚无产品 UI/实机跨端验收 |

当前输入源已用有限 buffer 复制到私有、可 seek 暂存并计算容器 SHA-256；SQLite 导出端和 ZIP 交付复验为分页／流式。`IGachaPortablePackageReader`、Plan/conflict 与 Apply mutation 准备仍会物化整个包，不能宣称为 2,000,000 条记录的端到端有界内存执行器，也尚未取得 100k 双端实测。磁盘关联索引与分页 Plan/Apply 仍是 8B.4 的未关闭项；格式保持未发布。

## 结构化错误

`GachaPortableErrorCode` 当前覆盖：包无效、不支持版本/枚举/账号、必需字段缺失、重复或无效引用、无效路径、意外/缺失条目、长度/哈希/记录数/记录内容不一致、资源超限和输入不可寻址。

错误包含可用时的载荷路径和记录 ID；不得包含 Cookie、SToken、AuthKey 或其他秘密。
