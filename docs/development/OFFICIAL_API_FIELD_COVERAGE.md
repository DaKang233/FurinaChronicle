# 官方 API 全字段解析：实施差距

日期：2026-11-23；基线 master / 1af7daa；状态：静态核查，尚未完成适配。契约见 [ADR 0032](../decisions/0032-official-api-record-completeness.md)。

维护者已确认全局规则，不存在等待再次确认的产品决定。以下是工程缺口，不把 Accepted 规范等同于当前代码已合规。此轮仅改文档／Agent 准则，没有改 Reader、业务 Schema、公开格式或用户数据；原有未提交修改保留。

## 米游社角色详情字段清单

本表记录当前 Reader 接受的成功响应字段。字段类型与去向以强类型 Reader 为准，后续用脱敏真实响应核对增量。三种响应的外层均采用 retcode（整数）、message（字符串或空）及 data（对象）；前两者用于控制流程。角色接口中的 data.list 必须存在且为数组。可选标量与分支的缺失或 null 保持未知，空数组和显式零保留原值。未知字段、重复属性和类型错误触发兼容失败。游戏属性值保留官方字符串，包括百分号和显示精度。

| 来源路径 | 字段与类型 | 映射与校验 |
| --- | --- | --- |
| character/list 的 data.list[] 和 character/detail 的 data.list[].base | id、level、actived_constellation_num（必需整数）；rarity、fetter、promote_level、weapon_type（可空整数）；name、element、icon、side_icon、image、card_image（可空字符串）；is_chosen（可空布尔）；weapon（可空对象） | 名单体保存在 CharacterDetail.Roster，详情 base 写入主字段。ID 必须大于零并且不重复，级数不得为负，命座数和突破为 0～6。名单与详情的角色 ID 集必须逐批一致。 |
| 名单武器、详情的 base.weapon 和 weapon | id（整数）；type、rarity、level、affix_level、promote_level（可空整数）；name、icon、type_name、desc（可空字符串）；main_property、sub_property（可空属性对象） | 分别保存到 Roster.Weapon、BaseWeapon 和 Weapon。详情 weapon 缺失时使用 base.weapon。武器 ID、等级、精炼及突破通过范围校验。名单中出现的武器必须有等级和精炼，按种类汇总时仅声明已装备范围。 |
| data.list[] 的 selected_properties、base_properties、extra_properties、element_properties，以及武器的主副属性 | property_type（必需整数）；base、add、final（可空字符串） | 保存为 CharacterStat，四个列表按来源独立保存，武器面板分别保存 MainStat、SecondaryStat。 |
| data.list[].constellations[] | id（必需正整数）；pos（可空整数，1～6）；name、icon、effect（可空字符串）；is_actived（可空布尔） | 命座 ID、位置、官方机制和解锁状态。can_enhanced、is_enhanced（可空布尔）及 enhanced_effect（可空字符串）保存为强化证据。 |
| data.list[].skills[] 及其 skill_affix_list[] | skill_id（必需正整数）；skill_type、level（可空整数）；desc、icon、name（可空字符串）；is_unlock（可空布尔）；倍率项为 name、value（可空字符串） | 天赋类型、等级、图标、解锁、介绍和倍率，保留数组顺序。can_enhanced、is_enhanced（可空布尔）、enhanced_desc（可空字符串）及 before_enhanced_skill_attr_index、after_enhanced_skill_attr_index（可空整数数组）保存为强化证据。 |
| data.list[].relics[]、其 set、set.affixes[] | relic 的 id（必需正整数）、pos、rarity、level（可空整数）、name、icon、pos_name（可空字符串）；set 的 id（必需整数）、name（可空字符串）；套装效果的 activation_number（可空整数）、effect（可空字符串） | 保存圣遗物部位、等级、套装与套装效果。 |
| data.list[].relics[] 的 main_property、sub_property_list[] | property_type（必需整数）；value（可空字符串）；times（可空非负整数） | 主副词条及官方命中次数，缺失次数与次数为 0 可区分。 |
| data.list[].costumes[]、unlock_tps、weapon_skin | 时装 id（必需整数）、name、icon（可空字符串）；unlock_tps（可空布尔）；幻化 weapon_skin_name、weapon_skin_icon（可空字符串）、weapon_skin_rarity（可空整数） | 保存在 Costumes、IsTpsUnlocked 和 WeaponSkin，展示可按客户端支持逐步接入。 |
| data.list[].recommend_relic_property 和 data.relic_property_options | recommend_properties、custom_properties（可空对象），has_set_recommend_prop（可空布尔）；各推荐对象的 sand_main_property_list、goblet_main_property_list、circlet_main_property_list、sub_property_list（可空整数数组） | 独立保存官方推荐、自定义推荐和属性选项目录。 |
| data.property_map 与 data.relic_wiki、weapon_wiki、avatar_wiki | 属性字典值的 property_type（必需整数）、name、icon、filter_name（可空字符串）；Wiki 为字符串字典 | 一个快照保存一份目录。属性键与 property_type 一致，详情批次间相同键的内容不能冲突。 |
| 养成计算器 sync/avatar/list 的 data 和 data.list[] | total（可空非负整数）；list（数组）；角色 id（必需正整数）；weapon_cat_id、avatar_level、element_attr_id、max_level、level_current、level_target、promote_level、constellation_num、fetter_level（可空整数）；name、icon、wiki_url、wiki_recommend_weapon_url（可空字符串）；skill_list、weapon、reliquary_list（可空数组或对象） | total、页大小与 ID 用于分页完整性校验。通过分页校验后，同一角色的字段保存为 AscensionEvidence。突破优先使用详情及名单明确值，计算器用于补充。 |
| 养成计算器 skill_list[]、weapon、reliquary_list[] | 各自 id（必需整数）；name、icon（可空字符串）；level_current、level_target、max_level（可空整数）；各自的 group_id，或 weapon_cat_id、weapon_level，或 reliquary_cat_id、reliquary_level（可空整数） | 保存为独立计算器证据，不覆盖详情天赋和已装备武器、圣遗物事实。 |

无角色名单与详情解析失败分别处理。详情请求是一个保存批次，任何一批失败均不提交该次新快照。计算器是独立补充证据，其失败时保留已完整解析的详情，无法证明的突破保持未知。Snap.Metadata 补充显示用元数据，原始官方快照与导出保留采集事实。对应边界见 [角色历史规范](../specifications/CHARACTER_WEAPON_HISTORY_V1.md)。

## 已核对的入口

| 路径 | 当前证据 | 下一步／验收缺口 |
|---|---|---|
| 米游社角色列表、详情及养成计算器 | 2026-11-27 已加入强类型 Response、未知字段拒绝、角色 ID 集校验、不可变快照和便携保真机制。详情映射面板、技能、命座、武器、圣遗物、词条次数及字典。计算器有界分页按角色 ID 提供突破补充证据。 | 合成响应及分类 Portable 往返已验证；需用真实响应校准可选字段与分支。已装备武器汇总仍只覆盖装备。HoYoLAB 沿用概况采集。 |
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
- 基线审计未运行采集或设备验收。2026-11-27 的角色代码另已完成自动测试，两端构建与真实设备交互记录分别汇报。
