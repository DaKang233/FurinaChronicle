# 服务端扩展

状态：Target

## 原则

服务端是可选扩展。客户端 Core 和本地数据必须在服务端不可用时保持正常。

云备份、自动同步、元数据和社区统计是四个独立能力，不使用一个含义模糊的“上传数据”接口。

## 客户端端口

```text
IRemoteBackupStore
    Upload / Download / List / Delete Archive

ISyncTransport
    Push / Pull ChangeSet / Cursor / Capabilities

IMetadataSource
    Manifest / Dataset / Asset

IStatisticsPublisher
    Preview / Publish / Revoke Submission
```

具体云厂商、WebDAV、S3、局域网或 Furina Cloud 通过适配器实现。

## 服务端模块

```text
Identity
Backup
Sync
Metadata
Analytics
```

- `Identity`：用户、设备和访问令牌。
- `Backup`：保存不可变 Archive 对象及目录信息，不必解析业务记录。
- `Sync`：ChangeSet、设备游标、Tombstone 和能力协商。
- `Metadata`：公开版本化元数据，不强制登录。
- `Analytics`：独立授权的统计投稿和聚合。

服务端可以只部署部分模块。客户端通过协议和能力版本判断可用功能。

## 隐私边界

- Backup 不自动成为 Analytics 数据源。
- Analytics 不接收凭据或整个私人 Archive。
- 社区统计默认关闭，上传前展示内容和用途。
- 删除 UID 不等于完全匿名；精细抽卡历史仍可能具有唯一性。
- 未来 E2EE 必须先完成威胁模型、密钥恢复、设备撤销和防回滚设计。

## 当前只保留的扩展空间

Phase 8 只需稳定 Portable Model 和上述端口边界，不要求实现账号系统、对象存储、E2EE 或社区统计。服务端技术选型可继续采用 ASP.NET Core、PostgreSQL 和 S3 兼容对象存储，但选型不进入客户端公开格式。

卡池事件元数据首先以内置版本化数据集提供离线基线。未来 Metadata 模块可以发布带格式版本、内容哈希、区服范围和来源修订的更新清单；客户端校验后将数据原子保存为本地可更新快照，并在失败时继续使用内置基线。横幅图片不属于数据集完整性的一部分，只通过资源 URL 进入可清理缓存。
