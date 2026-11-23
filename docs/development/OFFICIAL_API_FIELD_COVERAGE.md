# 官方 API 全字段解析：实施差距

日期：2026-11-23；基线 master / 1af7daa；状态：静态核查，尚未完成适配。契约见 [ADR 0032](../decisions/0032-official-api-record-completeness.md)。

维护者已确认全局规则，不存在等待再次确认的产品决定。以下是工程缺口，不把 Accepted 规范等同于当前代码已合规。此轮仅改文档／Agent 准则，没有改 Reader、业务 Schema、公开格式或用户数据；原有未提交修改保留。

## 已核对的入口

| 路径 | 当前证据 | 下一步／验收缺口 |
|---|---|---|
| `src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/MiYouSheCharacterWeaponResponseReader.cs` | Character DTO 仅 Id、Level、ActivedConstellationNum、Weapon；Weapon 仅 Id、Level、AffixLevel；只按有限字段生成条目，装备武器汇总为 Partial | 对照所调用接口完整响应补齐各层 DTO 与字段清单，设计角色详情和装备结果的可靠承载；不得将装备子集称为完整仓库 |
| `src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/MiYouSheSpiralAbyssResponseReader.cs` | 显式有限 AbyssData／Floor／Level／Battle DTO；JsonOptions 未设置未知成员拒绝或其他全字段覆盖证明；此前已将新观察与楼层标 Complete | 补齐正式响应字段及嵌套分支对照；校验和映射完整后验收新成功门槛 |
| `src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/MiYouSheRoleCombatResponseReader.cs` | 显式有限 Schedule／Stat／Detail／Round 等 DTO；JsonOptions 默认反序列化行为不是全字段证据 | 补齐本期／上期、概要／明细、普通／圣牌和候补／统计分支的全字段 fixture 与持久化对照 |
| `src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/MiYouSheHardChallengeResponseReader.cs` | 显式有限 Schedule／单人／联机／增辉／强敌 DTO；已有机制文本和大数测试不证明所有源字段覆盖 | 补齐各可选分支及未知字段门禁，验证完整解析后仍保留缺失／null 与未知发生时间 |
| `src/FurinaChronicle.Infrastructure/Gacha/Refreshing/MiHoYoGachaLogClient.cs` | JsonDocument 选取 list 中的指定字段；没有逐字段消费清单；ParsePage 将缺 data/list 情况返回空页 | 对照正式响应记录和分页／控制字段；区分有效空页与结构错误，补齐全字段校验及后续字段承载，不能据空结果误报成功 |

此表不宣称已审计全部官方接口。后续涉及其他游戏数据、账号资料或公共元数据的接入同样遵守 ADR 0032；登录／验证／凭据响应仍由安全适配器处理，秘密不能因全字段要求进入游戏事实或公开导出。

## 安全实施顺序

1. 为每个实际接口收集去敏样例，并核对已用参考实现；建立递归字段清单，明确业务事实、传输控制、公共缓存和安全敏感字段的去向。
2. 先冻结新增领域承载与适用的 Portable／Archive 版本，验证旧记录、旧指纹与旧包兼容；不使用数据库行或未审查原始响应替代领域模型。
3. 补齐 Response、校验、映射、持久化；加入未知字段检测。不能只加 JsonExtensionData 后宣称字段语义已支持。
4. 在已支持真实响应、分支和字段 fixture 验证后接入成功门禁；未完整解析的结果不能宣称采集成功，也不能提交完整事实。任何临时不支持要受控报告。
5. 单独验证来源记录 Complete 与更大集合覆盖的区别。不能先统一覆盖所有集合标记，再补充解释未取到的数据。

## 当前证据边界

- 前一轮 91 项挑战定向测试通过，仅证明所用 fixture 的内容、Complete 标记、历史保真及既有采集规则。
- 全字段覆盖：Not verified；全部新门禁：Not implemented。
- 本轮是文档变更，未运行新单元测试、真账号采集或设备验收；不重新使用旧测试／构建回执声称新规范已完成。
