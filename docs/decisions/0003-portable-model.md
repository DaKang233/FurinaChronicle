# ADR 0003：Portable Model 独立于 SQLite

状态：Accepted

可移植数据使用独立、版本化 DTO。不得把 SQLite Row、MAUI 文件对象或平台路径直接作为公开格式。数据库迁移和文件格式迁移独立演进，并使用显式 Codec、Validator 和 Migrator。
