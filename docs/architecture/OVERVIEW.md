# 架构总览

状态：Target

## 分层

```text
External API / File / Local Collector
                  ↓
              Source DTO
                  ↓
             Domain Model
             ↙          ↘
Persistence Model      Portable Model
SQLite                 ├─ UIGF / UIAF
                       ├─ Furina Dataset
                       ├─ Furina Archive
                       ├─ Markdown Report
                       └─ Sync ChangeSet
```

## 项目职责

- `FurinaChronicle.Core`：身份、领域记录、值对象和纯领域规则。
- `FurinaChronicle.Services`：用例、端口、导入计划、合并策略和事务边界。
- `FurinaChronicle.Infrastructure`：SQLite、HTTP、JSON、压缩、文件格式和外部 Provider。
- `FurinaChronicle.App`：MAUI 页面、平台文件选择、导航和用户交互。

Core 不得引用 MAUI、SQLite、平台存储、HTTP 客户端或具体外部 DTO。

## 数据层级

```text
Source Evidence
      ↓
Normalized Record
      ↓
Derived Record
      ↓
Projection
```

- 来源证据说明数据来自哪里。
- 规范化记录统一内部结构，但不代表绝对真实。
- 派生记录由基础数据计算或推断。
- 投影面向查询、UI、统计和报告，可随时重建。

## 当前实现与目标模型

当前数据库使用 `PlayerArchive → GameAccount → WishRecord`，`WishRecord` 通过 `GameAccountId` 归属档案内账号副本。目标模型保留该归属方式，同时给 `GameAccount` 增加共享的 `GameRoleIdentityId`，用于识别不同档案中的副本来自同一个真实角色。该迁移尚未实现。

服务端能力通过端口扩展，Core 和本地数据库不依赖服务端存在。
