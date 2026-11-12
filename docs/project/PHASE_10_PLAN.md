# Phase 10 实施计划与进度

2026-11-12。目标：把 Phase 9 采集到的角色／武器和三类挑战数据产品化。路线图见 [ROADMAP](ROADMAP.md)。

## 已确认的产品决策

- 详情页默认展示该账号在该模式下**最新一次观察**；顶部提供历史观察切换（按观察时间倒序），列表仍可分页。
- 元数据采用多数据源：**Snap.Metadata 为首要来源**，不可用时回退 YSHelper。Snap.Metadata 为远程 GitHub 仓库，参考胡桃 `Service/Metadata` 的目录结构：`Genshin/<locale>/{Avatar/*.json, Weapon.json, RoleCombatSchedule.json, HardChallengeSchedule.json, Monster.json, Meta.json}`。
- 图片使用官方 CDN URL；离线或缺失时降级为文字／占位，不打包第三方图片。
- 幻想真境剧诗与幽境危战保存并展示：期次、周期、难度、奖章／最高秒数，以及逐幕阵容与增益、逐怪名称／秒数／敌人信息。

## 工作包

| 工作包 | 内容 | 状态 |
|---|---|---|
| 10.1 角色／武器 | 官方 `character/list` 传输、观察与比较、历史页、隔离验证 | 已完成；2026-11-12 在线通过（角色 63 / 武器 36） |
| 10.2a 挑战模式采集 | `role_combat` 与 `hard_challenge` 传输、领域模型阶段字段、存储载荷扩展、模式切换 UI、隔离验证 | 已完成；在线通过（剧诗第 31 期 8 条、危战第 5269013 期 3 条） |
| 10.2b 挑战模式详情 | 逐幕阵容／增益、逐怪敌人等细节采集与展示；默认最新 + 历史切换；刷新自动匹配通行证对应角色 | 已完成；在线刷新与详情展示通过 |
| 10.3 版本化元数据 | Snap.Metadata 远程获取与本地缓存版本清单；不可用时回退养成计算器元数据；期次名称与周期解析 | 已完成（Snap.Metadata 主源 + 计算器回退；图标 URL 基址待确认） |
| 10.4 导出与 Chronicle | 领域 Portable v1 对新模式的扩展与校验；角色／武器与挑战的 Markdown 导出；Chronicle 只消费叙事节点 | 已完成：Portable 1.1（三模式＋阶段详情）、Markdown 覆盖、Chronicle 完成节点仅螺旋 |
| 10.5 收尾 | 全量回归、双端与 VS 构建、退出评审、版本 `0.10.0 / 10.0` | 已完成；见[退出评审](PHASE_10_EXIT_REVIEW.md) |

## 10.2a／10.2b 实现记录

- 领域：`ChallengeObservation` 增加可选 `Stages`、`Difficulty`、`MedalCount`；螺旋仍使用楼层结构，其他模式使用通用阶段。旧 JSON 载荷缺少新字段时按空值读取，内部快照格式保持 v1。
- 存储：`SqliteGachaAtomicChangeStore` 取消仅深境螺旋限制；相邻同内容仍为 NoOp，载荷比较包含新字段。
- 传输：`GameRecordRequestFactory` 统一主机、Cookie、指纹、DS 与 20 秒超时；`HoYoGameRecordChallengeModeSource` 分发剧诗／危战。
- 映射：剧诗保存每幕奖章与完成时间；危战保存每怪名称与官方秒数，完成时间保持 Unknown。
- UI：挑战历史页增加模式选择，期次选择仅对深境螺旋生效；卡片按模式展示摘要与阶段。
- 回归：新增 14 项模式传输／采集／验证测试；全量 746 项通过。

## 待明确或实施的技术点

- Snap.Metadata 拉取端点已冻结为 GitHub raw `Genshin/CHS`，缓存按 `Meta.json` 哈希分版本（见 10.3 记录）。
- YSHelper 回退契约仍未确认；当前角色／武器回退到现有养成计算器元数据库，待提供 YSHelper 元数据端点后再接入。
- 剧诗阵容／增益、危战敌字段与 Portable 扩展已在 10.4 完成（格式 1.1）。
## UI 重构适配约定

- 挑战与角色历史的页面逻辑已集中到页面内含可复用卡片/描述函数；后续 UI 重构（WinUI3 原生线）应把**数据查询与展示模型**下沉到可复用层，页面只负责布局与交互，不复制采集/查询代码。
- 现有 MAUI 页面作为过渡入口保留；WinUI3 页面接入时复用同一服务（`IArchiveDomainHistoryStore`、`CollectChallengeMode`、`CollectCharacterWeapon`、`Validate*`），不直接访问 SQLite。
- 观察切换采用“默认最新 + 下拉切换旧观察”，WinUI3 版使用同一语义（ListView 选中首个最新记录）。
## 10.3 实现记录

- `SnapMetadataClient`：GitHub raw `Genshin/CHS`，`Meta.json` 24 小时检查一次，以其 SHA-256 前 16 位作为缓存版本目录；网络失败回退到最后缓存版本；单文件上限 16 MiB。
- 解析：`Avatar/<id>.json`（名称、图标名、稀有度）、`Weapon.json`、`RoleCombatSchedule.json`、`HardChallengeSchedule.json`（名称与周期）。
- `CompositeDomainMetadataService`：Snap.Metadata 优先；角色／武器缺失时回退现有养成计算器元数据（`IGachaItemMetadataProvider`）；挑战期次只由 Snap.Metadata 提供。
- UI 展示模型下沉到 `FurinaChronicle.Presentation/History`，供 MAUI 与后续 WinUI3 复用；角色页新增“历史观察”切换，在线实测名称解析成功（琴、丽莎、旅行者等）。
- 待确认：YSHelper 的具体元数据端点（本地参考仅见抽卡／排行，没有物品元数据接口）；图标名到官方 CDN 的 URL 规则（当前临时使用 Enka 镜像，需替换为官方规则）。
## 10.4 实现记录

- 挑战 Portable 升至 `1.1`（ADR 0024）：接受深境螺旋／剧诗／危战三模式，携带阶段 ID、分数、完成时间、阵容、增益、敌人以及难度／奖章；旧 `1.0` 可读，无 Migrator。
- Apply 路径不再拒绝非螺旋模式，并按阶段详情重建不可变观察；内容比较、语义摘要与 Markdown 同步覆盖。
- 回归：三模式导出／读取／导入往返、Markdown 含阵容／增益／敌人、Chronicle 不产生非螺旋完成节点；全量 752 通过。
- Windows Debug、Android Release（`net10.0-android`）、VS 2026 全解决方案构建通过；`git diff --check` 与版权头检查通过。