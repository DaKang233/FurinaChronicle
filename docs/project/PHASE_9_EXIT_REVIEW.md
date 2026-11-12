# Phase 9 退出评审

2026-11-12。**Phase 9 功能验收关闭。** 里程碑 `0.9.0 / 9.0`，业务数据库 Schema 12，内部 Archive 与领域 Portable 格式不变。本轮不修改可移植格式，只把 8B 的本地纵切接成可实际刷新的 Provider，并补齐角色／武器采集。

## 功能退出条件

| 条件 | 结果与证据 |
|---|---|
| 小能力接口与角色解析 | 采集使用 `IMiHoYoAccountProfileClient`、`IDeviceFingerprintProvider`、`ISpiralAbyssSource`、`ICharacterWeaponSource`；每次提交重新核对档案、账号副本和唯一角色绑定 |
| 国服深境螺旋 Provider | 当前期／前一期、米游社与 HoYoLAB 路由、`1034` 极验（index 优先，验证作用域为 index）、`5003` 账号风险分类、`10306` 提交拒绝路径均有回归；2026-11-12 用健康账号在线通过隔离采集（第 124 期 · 34 星） |
| 海外深境螺旋 | HoYoLAB 传输、Cookie 后缀、期次与跨区服拒绝由合成测试覆盖；真实海外账号在线验证仍为 `Not run` |
| 国服角色／武器 Provider | `character/list` 映射为角色完整名册与已装备武器部分集合；重复采集不变时 NoOp；2026-11-12 在线隔离验证通过：首次写入、后续刷新无变化、重开读回 2 条（角色 1 / 武器 1），快照为角色 63 条、武器 36 条 |
| 指纹与设备上下文 | 大陆交互登录与启动维护注册并保留设备指纹；战绩与角色请求发送 `x-rpc-device_id`／`x-rpc-device_fp`；真实账号在线通过 |
| 统一边界与证据 | 单次尝试、20 秒网络超时、响应上限 2 MiB（角色／深渊）与 4 MiB（index 验证缓冲）；`Retry-After` 结构化返回；每次观察独立 `AcquisitionBatchId`；不落盘原始响应、不记录 Cookie／令牌／验证码／指纹值（D09） |
| 失败与限流 | 合成覆盖 401／403／429／503、错误 JSON、非零业务码、超限与取消；未知业务码受控拒绝 |
| 工程验证 | Release 全量 **732** 项测试通过，0 失败、0 跳过；Windows x64 MAUI Debug、Android TFM 与 Windows 项目、VS 2026 MSBuild 全解决方案 Debug、Phase 9 探针 Release 构建通过；`git diff --check` 与版权头检查通过 |

## 证据索引

| 证据 | 位置 |
|---|---|
| 采集契约、`10306`／`5003` 复核、在线验收与根因记录 | [Phase 9.0／9.1 实施记录](PHASE_9_COLLECTION_IMPLEMENTATION.md) |
| 深境螺旋传输与映射 | `FurinaChronicle.Infrastructure/HistoryDomains/Collecting/HoYoGameRecordSpiralAbyssSource.cs`、`MiYouSheSpiralAbyssResponseReader.cs` |
| 角色／武器传输与映射 | `HoYoGameRecordCharacterWeaponSource.cs`、`MiYouSheCharacterWeaponResponseReader.cs` |
| 采集用例 | `CollectSpiralAbyss.cs`、`CollectCharacterWeapon.cs`、`ValidateSpiralAbyssCollection.cs`、`ValidateCharacterWeaponCollection.cs` |
| 回归 | `HoYoGameRecordSpiralAbyssSourceTests`、`HoYoGameRecordCharacterWeaponSourceTests`、`CharacterWeaponCollectionTests`、`SpiralAbyssCollectionTests` |
| 入口 | MAUI「挑战历史」与「角色与武器」页；`eng/phase9/FurinaChronicle.Phase9Probe` 命令行探针 |

## 留项与边界

- Android 真机交互未运行；本轮在线验收在 Windows x64 MAUI 实际页面上完成。
- 海外深境螺旋与角色数据未用真实海外账号验证；现有证据为合成测试。
- 旧授权账号 `274079782` 在本机处于“机器＋账号”风控（米游社网页服务在本机也不可访问，手机端正常），不影响 Provider 结论；验收改用健康账号 `400379797` / 角色 `291245883`。
- 单次尝试是明确策略：失败由调用方决定重试，不在传输层自动重放风险请求。
- 幻想真境剧诗与幽境危战的采集与产品化属于 Phase 10；Phase 9 的挑战 Provider 只承诺深境螺旋。

## 交接

Phase 10 可在现有 `GameAccountId` 副本、领域历史存储和 Portable v1 边界上继续：角色／武器长期观察、比较、查询与 UI，三类挑战历史，版本化元数据，领域导出与 Chronicle 叙事消费。采集传输与隔离验证入口已具备，无需再改身份、时间或来源语义。
