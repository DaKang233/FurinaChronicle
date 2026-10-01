# 术语表

状态：Accepted

## Gacha / 抽卡记录

项目内部领域类型、命名空间、文件名、数据库当前 Schema、服务接口、测试和文档统一使用 `Gacha`，不使用 `Wish` 表示抽卡记录。`GachaRecord` 表示一条规范化抽卡记录，`GachaRecords` 是当前数据库表名。

只有必须兼容的历史持久化标识或外部协议原始字段可以保留 `Wish`。当前唯一内部兼容例外是数据库 Schema v1 的旧表 `WishRecords` 及其索引；它们只允许出现在 v1→v2 迁移实现和迁移测试中，不得用于新增代码。

## PlayerArchive / 玩家档案

用户定义的数据集合和分析边界。一个档案可以包含多个游戏账号副本。档案名称默认要求精确唯一，但可以关闭该选项。

## GameRoleIdentity / 游戏角色身份

全局尽量稳定的 GUID，用于说明不同档案和设备中的账号副本来源于同一角色；同时保存 `GameBiz + Region/Server + UID` 自然身份。

## GameAccount / 档案内游戏账号副本

当前代码类型名。其 `Id` 表示某个档案中的独立数据副本，业务记录以该 ID 为外键。相同角色在不同档案中的 `GameAccount.Id` 不同，数据完全独立。

## ProviderAccount / 平台账号

米游社、HoYoLAB 等外部平台账号，负责取得角色列表和新数据，不拥有业务历史。

## CredentialProfile / 凭据配置

Cookie、SToken、OAuth 信息和相关设备参数，生命周期短于游戏角色身份。

## Source Evidence / 来源证据

官方响应、标准文件、Furina 数据集、本地采集或手工输入等直接输入。

## Normalized Record / 规范化记录

将来源证据转换为 Furina 统一领域结构后的记录；规范化不表示内容绝对真实。

## Derived Record / 派生记录

通过差分、统计或推断生成且可重新计算的结果，不能冒充官方事实。

## Projection / 投影

为查询、统计、UI 或报告生成的读取模型。

## Game Chronicle / 游戏历程

由各业务数据域的规范化记录和明确派生事实生成的跨域时间投影，用于回答游戏账号在何时发生或呈现了什么。它不是新的事实来源。

## Archive Audit Timeline / 档案操作历史

导入、刷新、覆盖、纠正、删除、撤销、备份和同步等应用操作的历史。它与游戏历程分离，`ImportedAt` 或操作时间不得冒充游戏事实发生时间。

## ChronicleEntry / 历程条目

指向某个规范化记录或派生事实的可重建读取模型，包含稳定身份、账号归属、时间范围、来源、置信度、完整度和展示摘要。

## TemporalExtent / 时间范围

统一表达瞬时事件、确定区间或两次观察之间的不确定区间，并保留精度、时间依据和来源偏移。

## DataChangeSet / 数据变更集

一次用户可理解的数据修改操作。一个跨档案原子操作仍是一个 ChangeSet。

## OperationHistory / 操作历史

面向用户、按档案计数的有限撤销记录。默认 3 次，可关闭，允许设置 1～1000 次。

## ChangeJournal / 变更日志

未来同步使用的可靠变更序列，与撤销数量无关。

## LocalOnlyTombstone / 本地删除墓碑

不可逆删除后保留的最小本地标记，不包含被删具体内容，也不传播到其他设备或云端。

## Portable Dataset / 可移植数据集

独立于 SQLite、UI 和操作系统的版本化机器可读数据。

## Furina Archive / Furina 全量档案包

包含清单、多个可移植数据集、校验信息和可选附件的全量备份容器。
