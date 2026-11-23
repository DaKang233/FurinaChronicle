# 挑战观察历史与 Portable

状态：Accepted implementation；内部格式未发布

本规范依赖 Accepted ADR 0017 与 ADR 0024。当前生产支持深境螺旋、幻想真境剧诗与幽境危战三类模式。

## 观察范围

期次身份由目标账号副本、稳定模式代码和官方期次 ID 构成。观察保存独立引用、规范内容指纹、周期（可空）、覆盖范围、来源、ObservedAt／FetchedAt 以及来源明确提供的子关卡事实时间。

`Complete` 只能相对于声明的期次、楼层／间和字段集成立。维护者于 2026-11-23 确认：从米游社／HoYoLAB 官方 API 成功取得并通过现有解析校验的每条单次挑战观察，统一记为 `DataCompleteness.Complete`；深境螺旋返回的楼层结果同样相对于该次响应声明 Complete。此范围是“本次返回的该期状态”，不是“通关、满星、所有阶段均有结果或账号历年记录齐全”。概要结果、仅联机结果和缺少可选字段仍遵守此规则；缺失字段、未知发生时间及未完成关卡保持原值，不补造内容。错误、风控、截断或校验失败仍不得保存为成功观察。

本决定仅作用于新的官方在线采集，不在启动／读取／导入时批量改写既有不可变观察的完整度或指纹。新采集与旧观察的完整度不同仍通过正常观察提交、相邻判重及冲突规则处理。文件导入／Portable／Archive 原样保留来源声明的 Complete／Partial／Unknown，不因为 provenance 为 OfficialApi 就重标；角色／武器等其他领域不受影响。单期 Complete 不表示账号挑战历史完整。

## 历史和当前视图

Portable 保存全部不可变观察。重复引用同内容为 NoOp，同引用不同内容为 Conflict；可信顺序证明的 A→B→A 保留第二个 A。迟到观察补充历史但不因导入时间成为当前最佳证据；同刻矛盾或缺少顺序证据使用结构化 Conflict／Ambiguous。

当前最佳证据视图是可重建派生结果，不替代历史。Partial 不覆盖 Complete，非包含关系 Partial 不自动合并为 Complete。

## 在线采集窗口

每次正式采集所选挑战模式时，检查官方当前可取得的本期和上期。深渊使用两个显式 period 请求；剧诗与危战读取响应中的两期期次，不仅取第一项。不据此承诺找回官方窗口外的历史，也不删除本地更早观察。

一次采集的有效期次共用 acquisition batch，经完整读取与校验后通过既有一次原子提交保存。不同期次保留各自范围、内容、FetchedAt／ObservedAt 与已有完成时间规则；无数据期次不补造记录，全部无数据则报告为空。请求／验证／解析失败或取消时不提交该次采集的部分新观察；重复或无法确定的期次不任取其一。保留原有判重、抑制与撤销规则。

危战强敌机制的源 desc／tags 富文本原样保存；显示可以跳过空白占位，不能截断为第一段、丢失顺序或将显示文字回写为事实。旧快照缺字段仍为未知；重新采集得到的机制按正常新增观察规则处理，不静默回填旧快照。

## 可读导出

首版提供 Markdown，按账号、模式和期次列出全部观察、周期、来源、楼层／间、星数、实际完成时间和未知项。不输出未经领域规则确认的“完成”节点，不用周期末或抓取时间填充 `OccurredAt`。

剧诗／危战按阶段展示分数、阵容、增益与敌人；未知完成时间保持未知；未支持模式仍受控拒绝。

深境螺旋以官方 `levels[].battles[].timestamp` 保存完成时刻。上下半均有唯一场次及有效 Unix 秒、且下半不早于上半时，保存下半时刻为该间的 `CompletedAt`，精确到秒。已有正星结果缺少可靠时间时保留该结果，时间为 Unknown。零星结果须有上下半的有效完成证据才纳入。剧诗使用官方 `rounds_data[].finish_time`，精确到秒。幽境危战在线观察的完成时刻为 Unknown，采集时间单独保存。旧观察保持原有内容，后续刷新按既有观察规则处理。

公开期次的敌方波次、数量、词缀、上下半异常和机关仅为展示元数据，不表示玩家实际击破或出战，也不补造完成时间或提升观察完整度。它们不进入本观察／Portable 格式；实现与缺失规则见 [挑战展示元数据](../architecture/CHALLENGE_DISPLAY_METADATA.md)。

## Portable 版本与深渊详情

深渊 `spiral_abyss_floors[].chambers[].battles` 可携带最多两个半场。`half` 值 1／2 对应上／下半，同一间不允许重复半场。阵容来自官方 `battles[].avatars`，敌人来自对应半场的 `top_half_floor_monster`／`bottom_half_floor_monster`。缺失详情保持为空。单个半场有可靠时间时可单独保存；间的整体发生时间仍遵循上面的双半场规则。

```json
{
  "half": 2,
  "completed_at": { "kind": 0, "start": null, "end": null, "precision": null },
  "participants": [{ "id": "10000089", "name": null, "icon_url": null, "level": 90, "rarity": 5 }],
  "enemies": [{ "name": "示例敌人", "level": 99, "icon_url": null }]
}
```

包含半场详情或官方统计的完整 Archive 使用挑战子数据集 `1.1`，并将该数据集标为必需。旧客户端会明确拒绝不能完整还原的 Archive。Archive 容器版本及不含上述详情的旧挑战子数据集 `1.0` 保持兼容。

- 机器契约见 [JSON Schema](schemas/challenge-history-v1.schema.json)；格式名 `furina-domain-history`，`dataset_kind = 2`，版本 `2.0` 保存深渊半场详情与官方统计，`2.1` 增加下述剧诗详情，`2.2` 增加危战／命座详情，2.3 增加剧诗结构化卡牌／祝福；无这些详情的数据仍导出为 `1.1`，旧 `1.0`／`1.1` 可读，无需迁移。旧读取器接受未知次版本并会忽略新增字段，因此新增深渊详情使用新的主版本，以免旧客户端导入时静默丢失。
- `mode` 接受 `1` 深境螺旋、`2` 幻想真境剧诗、`3` 幽境危战；其他值拒绝。
- 观察可选携带 `stages`（阶段 ID、分数、完成时间、阵容、增益、敌人）、`difficulty`、`medal_count`；缺失按空值读取。
- Chronicle 完成节点仍只由 ADR 0019 的深境螺旋 12-3 规则产生；剧诗／危战不产生完成节点。
- Portable 保存全部不可变观察和 provenance，不保存本机 Undo、Tombstone、操作重试结果或派生“当前”视图。
- 同 ObservationId 同业务内容、同稳定来源记录重试及相邻可信时点同内容均为 NoOp；同来源不同内容或同可信时刻矛盾为 Conflict；可信 A→B→A 保留。
- Plan 与 Apply 使用同一判重语义；目标改变、指纹改变、未知顺序或 future major 均在提交前拒绝。父实体、观察、Receipt 和 ChangeSet 同事务。
- 资源上限与角色／武器数据集相同：单文件 10 MiB、最多 100,000 个观察；当前没有旧版 Migrator。

### 官方深渊统计

可选 `spiral_abyss_statistics` 与半场详情采用相同版本门槛。计数缺失为 null，不从已有楼层补齐。排行按官方顺序保存，`value` 为非负 64 位整数，不能截断为 32 位。此处是当期官方汇总，不宣称覆盖该账号历年记录，也不据此推断完成时间。不能附在非深渊模式上。

```json
{
  "total_battle_times": 17,
  "total_win_times": 12,
  "max_floor": "12-3",
  "total_stars": 34,
  "reveal_rank": [{"avatar_id": "10000089", "value": 12, "icon_url": null, "rarity": 5}],
  "defeat_rank": [],
  "damage_rank": [{"avatar_id": "10000089", "value": 3000000000, "icon_url": null, "rarity": 5}],
  "take_damage_rank": [],
  "normal_skill_rank": [],
  "energy_skill_rank": []
}
```

可实际读取并校验指纹的完整合成示例见 [challenge-history-v2-abyss-statistics.json](examples/challenge-history-v2-abyss-statistics.json)。

六个排行分别为出战、击破、最强一击、承伤、元素战技和元素爆发。SQLite 私有载荷、Portable、完整 Archive、内容判重、恢复摘要与 Markdown 均保留上述字段。恢复映射必须连同既有的 stages／difficulty／medal_count 处理，不能只保留楼层。旧观察不回填，Schema 保持 12。

## 剧诗完整详情（内部 2.1）

可选 `theater_statistics` 保存官方汇总和实际候补角色。字段为 `max_round_id`、`heraldry`、`coin_count`、`avatar_bonus_count`、`rent_count`、`tarot_finished_count`、`total_use_time`（秒）、`show_battle_stats`、`medal_rounds`、`backup_avatars`、`max_defeat`、`max_damage`、`max_take_damage`、`total_coins_consumed` 和 `shortest_avatars`。未知计数为空；数组缺失不宣称完整覆盖。

角色表现为 `{ "participant": { "id": "...", "name": null, "icon_url": null, "level": null, "rarity": 5 }, "value": "3000000000" }`。字符串值不截断为 Int32；采集边界规范化数值表示，官方明确的空串值表示零，缺字段保持 null。隐藏标记为 false 时 UI 和 Markdown 不展示角色表现，机器格式保留收到的字段与标记。

阵容／候补角色的可选 `origin` 为 1 自有、2 试用、3 助演；缺失为空，不能据此默认判断自有，未知代码拒绝。它只描述该次挑战中的角色来源，不创建角色仓库记录。阶段可选 `theater_round` 为 `{ "is_tarot": true, "tarot_serial_number": 1 }`；普通幕与塔罗标记缺失均保留原含义，不推断未知精度或时间。发生时间仍来自 finish_time。舞台仅在展示时按数字幕次排序，持久化稳定 StageId 不变。 新的官方采集适配为塔罗幕使用稳定组合键 `tarot:<round_id>:<tarot_serial_no>`（序号缺失为 `unknown`），避免与普通幕共用源幕号时冲突；普通幕仍为数字幕号，非塔罗的 -1 序号表示不适用。已有观察／导入的 StageId 不回写，详见 [ADR 0026](../decisions/0026-spiral-abyss-battle-details.md#剧诗官方幕次兼容修正2026-11-20)。

这些字段至少要求分类挑战 2.1 或 Archive 必需挑战子数据集 1.2（及其受支持后继版本）；旧 1.x／2.0 分类包以及 1.0／1.1 Archive 子数据集携带它们均受控拒绝。旧载荷与旧 1.x 数据不回填。Summary-only 官方结果按本次单期响应范围作为 Complete 观察保存，不据汇总计数生成挑战完成节点。

完整合成文件见 [challenge-history-v2-1-theater.json](examples/challenge-history-v2-1-theater.json)，其指纹由正式导出器生成并由兼容测试读取。

初始／特邀角色和准入元素来自版本化期次元数据，展示时明确标为“期次配置”；它们不进入玩家所有权或 OfficialApi 观察事实。此元数据缺失时降级，不伪造角色名单。

公开发布仍需 Windows↔Android 文件语义往返；在此之前属于内部支持格式。

## 幽境危战详情（内部 2.2）

单人关卡仍为 stages；multiplayer_stages 为同一次观察中独立的联机关卡集合，各自按 StageId 判重，不相互合并。旧 StageId 不重写。单人／多人每关均保存阵容、敌人和耗时；官方没有给出完成时间时 CompletedAt 为 Unknown，不能从成绩、期次或抓取时间推导。

新增可选 stygian_statistics：

- single_player／multiplayer：has_data、difficulty、seconds（官方最佳总成绩）、icon_url。分支对象缺失或为空表示未知，不推断 false。明确 has_data=false 时不保存默认占位成绩，也不能同时有该分支关卡。
- bling_avatars：participant、可空 is_plus、可空 element。增辉／上榜信息不表示玩家持有角色，不生成仓库事实。

阶段可选 stygian_details 保存 monster_id、monster_descriptions、monster_tags（type／description）和 best_avatars（participant／value／type）。怪物描述保留来源文本，UI 可消除已知颜色标记以便阅读。角色表现值为非负 Int64；官方字段 dps 的类型 1 为最强一击，2 为最高总伤害，不将其解释为每秒伤害。未知非负指标代码保留并明确显示未知。缺失值为空，不补零。

participant.constellation 为可空 0～6，明确 0 命和未知不可混同；这里只描述该关卡的角色。观察的既有 Difficulty 是单人难度的兼容投影：有官方单人总览难度时保持一致，矛盾拒绝；没有单人数据时不能把联机难度写入它。UI 分别显示两类难度／总成绩，不以各关求和或最小值替代官方汇总。

这些字段只由分类挑战 2.2／Archive 必需挑战子数据集 1.3 承载。仅旧字段继续使用原最低版本；未知未来次版本以及旧版本冒称新字段均拒绝。仅联机结果或增辉信息也按本次单期响应范围保存 Complete 观察，但不补造完成节点。新增私有可选字段为空时不改变旧载荷；嵌入剧诗统计的旧角色 JSON 保留其 null 字段和顺序，未知命座不额外写出。

同一观察 ID 的联机成绩或关卡差异是冲突，不能被 Preview 误判 Skip；新可信抓取的相邻业务内容变化按既有规则形成新观察。单人／联机数据在同一原子提交、可移植映射、恢复摘要和可读导出中处理。

完整合成示例见 [challenge-history-v2-2-stygian.json](examples/challenge-history-v2-2-stygian.json)。该格式仍属内部支持，尚未完成真实账号及跨端新增字段验收。


## 剧诗结构化卡牌与祝福（内部 2.3）

theater_round 可选增加：

- buff_details：按来源顺序保留的列表。kind 为 choice_card、buff 或 splendour；必需 name。其余 source_id（非负整数）、description、icon_url、level（非负整数）、is_enhanced、effects 可空／缺失；未知标量不补默认值。effects 表示本次保存到的效果文字，缺失／空均规范化为空列表，不承诺完整效果目录，原顺序保留。
- buff_summary：来源的辉彩祝福总览文字，可空。缺少结构化内容时不写扩展属性。

来源 ID 仅在官方提供时保留；不为辉彩祝福生成伪 ID，也不使用名称或列表序号作为全局事实身份。同名卡牌不能因此合并。is_enhanced=false 与未知不同；缺图标仍能读取全部文本。已有 stages[].buffs 兼容文本继续保留，旧观察不补回已经丢失的来源字段。结构化内容的变化沿用相邻观察判重，单独抓取时间变化不产生新观察。

新增字段最低需要分类挑战 2.3、Archive 必需挑战子数据集 1.4。其他包保持原最低版本；冒称旧版本和未知未来版本在写入前拒绝。私有 Schema 仍为 12；新增属性缺失时旧 payload 和内容哈希不变。Portable Wire 显式映射类别字符串，不能直接序列化 SQLite Row。完整合成示例见 [challenge-history-v2-3-theater-buffs.json](examples/challenge-history-v2-3-theater-buffs.json)。

显示层去掉官方已知颜色／加粗／尺寸等样式标记并解码换行，保留内容；不会反写原始描述。图标使用既有受信任公共图片缓存，图片失败不阻塞文字。发生时间继续取官方 finish_time（秒），上述字段不改变完成规则或 Chronicle 节点。

仍为内部支持，未宣称真实账号新字段采集或 Windows↔Android 新增字段设备往返已完成。ADR 0026 记录兼容实施边界。
