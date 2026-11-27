# 米游社角色详情实施与验收

状态为已实施，验收日期为 2026-11-28。本记录针对原神大陆角色详情，数据规则遵循 ADR 0032 和角色／武器历史规范。

## 实施范围

- 官方角色名单与详情分批读取，角色 ID 集逐批匹配，全部校验后一次提交角色与已装备武器观察。
- 保存等级、角色突破、好感、命座与解锁、天赋等级和倍率、属性面板、武器等级／精炼／突破、圣遗物主副词条及官方命中次数、套装效果。
- 保存官方新增命座／天赋强化状态、强化说明与强化前后倍率索引。解析遇到未知字段、非法值或重复身份时返回受控失败。
- 官方显示信息优先，Snap.Metadata 补齐机制、图标和可用等级倍率。静态补充用于展示，官方快照保持原始采集语义。
- WinUI 采用观察／角色／详情三栏，支持命座锁标、天赋机制浮层、十项常用属性、展开全属性、绿色加成数值、十字星突破、圆角圣遗物卡片。
- MAUI Android 共享资料及导出模型，机制／全属性使用带关闭按钮的上滑附加层，支持系统返回。
- 当前角色及所选账号当前快照的全部角色可复制为 JSON。分类包按内容使用 1.x、2.0 或 2.1，完整备份的角色子数据集按内容使用 1.0、1.5 或 1.6。

## 验收证据

| 范围 | 结果 | 实际证据 |
| --- | --- | --- |
| 真实 WinUI 官方采集 | Pass | 通过正常界面入口采集获准国服角色。85 个角色全部保存详情，随后关闭应用重新打开。 |
| 真实快照重开 | Pass | 只读重新打开库确认 85 个角色都有好感、角色突破、武器突破和属性面板；85 个角色有天赋倍率及强化字段。83 个角色返回命座集合，71 个角色有已装备圣遗物的副词条命中次数。未返回的集合保持实际空值或空数组。 |
| WinUI 界面交互 | Pass | 隔离数据验证快照／角色切换、天赋浮层、单角色与全部角色剪贴板内容。真实数据重开后详情、全部属性入口和两种复制入口可见。 |
| Android 模拟器 | Pass | 隔离 Android 用户验证角色详情、天赋倍率、全部属性、X 关闭和系统返回。 |
| Android 真机 | Pass | 已连接 vivo 真机使用独立包名 com.companyname.furinachronicle.profilevalidation，合成数据库验证角色页、天赋倍率、全部属性与系统返回。用户另确认 MAUI 已能从真实账号读取角色信息。 |
| 可移植保真 | Pass | 合成数据验证分类导出／导入、完整备份恢复、数据库重开、强化字段的版本门槛与幂等。相同快照 ID 下仅更改属性目录内容产生冲突，字典排列变化保持等价。 |
| 自动化 | Pass | 本地 Release 全量 1,495 项通过，结果 character-profile-complete.trx。GitHub 结果以实际提交对应的工作流为准。 |

本机原始验收回执位于工作区 .validation/character-profile-20261127，采用计数和布尔值记录结果。真实账号内容与凭据不进入本记录或测试 fixture。

## 计算器兼容修复

角色详情没有直接提供角色突破时，通过同账号、UID 和区服访问官方养成计算器。胡桃当前 CalculateClient、SyncAvatarFilter 与 Avatar／Calculable／PromotionDelta 用于对照请求与模型。

2026-11-28 实测中，Furina 缺少 User-Agent 和设备上下文的计算器请求返回 10041。补齐浏览器 User-Agent、Accept、设备 ID 及已有指纹后，同一账号连续 5 页均返回业务码 0，取得 85 个角色及其突破等阶。全部分页校验后才附加 AscensionEvidence，官方补充失败仍保留未知突破。

对应回归断言检查设备头、User-Agent、Accept 和 Referer。字段路径与类型依据真实成功响应校对，响应值与凭据未保存到测试代码。

## 相关代码

- src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/HoYoGameRecordCharacterWeaponSource.Ascension.cs
- src/FurinaChronicle.Infrastructure/HistoryDomains/Collecting/MiYouSheCharacterDetailResponseReader.cs
- src/FurinaChronicle.Core/Inventory/CharacterDetail.cs
- src/FurinaChronicle.Presentation/History/CharacterDetailPresentation.cs
- src/FurinaChronicle.Windows/Pages/CharacterWeaponPage.Details.cs
- src/FurinaChronicle.App/UI/Pages/CharacterHistoryPage.Details.cs
- src/FurinaChronicle.Tests/Infrastructure/HistoryDomains/HoYoGameRecordCharacterWeaponSourceTests.cs
- src/FurinaChronicle.Tests/Services/Archives/Portable/PlanArchiveRestoreTests.CharacterDetails.cs

范围仍以已返回的角色与装备观察为准。角色观察的 Complete 不推导账号全部仓库或历年历史完整。HoYoLAB 继续沿用既有概况采集，本次真实来源验证针对米游社。
