# 术语表

状态：Accepted

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
