# 挑战展示元数据

状态：Implementation（内部实现记录）。2026-11-19。不是新的事实格式、数据来源优先级或 Chronicle 契约。

## 事实与期次配置分离

挑战观察保存玩家的官方结果、阵容、时间与来源。公开期次元数据提供祝福、楼层异常、敌方配置和机关。两者在读取展示时组合，不将元数据转换为 OfficialApi 玩家事实，不补造实际击破、出战次数、完成、星数或完整度。

深渊／剧诗的已知完成时间继续来自官方秒级字段；幽境危战没有可靠发生时间时保持 Unknown。期次开始、结束或加载时间不能成为完成时间。分类 Portable 和 Archive 导出原有观察，不把这里的公共配置写入观察；Schema 和格式版本没有因本轮展示扩展改变。

## 深渊关联路径

1. 官方期次 ID → TowerSchedule.Id。
2. TowerSchedule.FloorIds → TowerFloor.Id。不能只按“第 12 层”搜索全表或拿另一期兜底。
3. TowerFloor.LevelGroupId → TowerLevel.GroupId；Index 为该层的间序号。
4. FirstWaves／SecondWaves、FirstGadget／SecondGadget 对应上／下半；FirstDescriptions／SecondDescriptions 是分半异常。
5. 波次中的 Monster.Id 是 DescribeId，按 Monster.DescribeId 查名称与图标。明确的展示别名只用于名称／资源查询，原始 DescribeId 保留。

仅展示当前观察已包含楼层的配置；该层尚无实战记录的间／半场可以显示配置，但明确标注“未保存战斗记录”，不增加领域中的间或半场。来自响应、已经保存的敌人和来自期次配置的敌人分别呈现，不按名称合并或相互冒充。

同一楼层序号／间序号的歧义配置不任取第一条；同一 DescribeId 若有互相矛盾的名称或图标，保持 ID，不任意挑一个名字。

## 缺失与旧结构

- 有 FirstWaves／SecondWaves 时使用波次结构，包括显式空数组。空数组不同于缺字段。
- 仅有 FirstMonsters／SecondMonsters 的旧结构显示敌人列表；波次与数量未知，不推定为独立刷新或每种一只。
- 已知零值和未知分开；负数量或等级视为未知。数量使用 Int64。
- 敌人数量、词缀、AttackMonolith、机关说明及波次描述均可展示。
- 已知波次代码对应独立、分组、增援等规则；未知代码显示原值，不拒绝整个期次。
- 缺表、无对应期次／楼层、坏 JSON 或可选缓存读取失败，保留已有玩家记录与可用元数据；不跨期次猜测。取消仍向上传播。

## 缓存与资源

沿用 SnapMetadataClient 的版本目录。Manifest 无法更新时可以使用已缓存版本；所选本机版本中某个文件不可用时明确缺失，不从另一份本机版本目录拼接关联表。客户端读取 TowerSchedule、TowerFloor、TowerLevel、Monster，普通重复查询复用内存表。当前客户端实例固定其已选版本，Manifest 检查间隔用于实例首次访问；不声称同一实例会持续热更新。目录隔离也不等同于已验证的上游原子快照：未缓存文件仍从配置的上游地址读取。

敌人图标默认使用 https://static.snaphutaorp.org/static/raw/MonsterIcon/，由 SnapMetadataOptions.MonsterIconBaseAddress 指定。实际探测发现原 api.snaphutaorp.org 路径会 302 到此静态域名；Enka 的同名怪物路径返回 404，不能用角色资源目录代替 MonsterIcon。角色继续使用官方响应或已解析元数据给出的公共图标地址。

Services.HistoryDomains.ChallengeArtworkUri 是展示和缓存共用的地址边界：HTTPS、默认端口、无凭据／查询／片段，受信任图片域名及扩展名；胡桃 API／静态域名只接受 MonsterIcon 精确目录和安全文件名。ChallengeArtworkRedirectHandler 禁止底层自动重定向，仅允许最多 3 次受同一规则验证的跳转，不携带 Cookie、Authorization、设备 ID 或玩家 UID。

FileChallengeArtworkCache 是可选资源适配器，复用 FileGachaItemIconCache 的有界下载、体积上限、图像签名、临时文件和原子替换；不建立另一套事实或公开格式。
- 独立 challenge-artwork 子目录，按规范公共 URI 的哈希保存同一文件。
- 30 天尝试更新；旧图立即可用，刷新失败保留旧图；失败后 10 分钟内不因重复出现同一资源而不断重试。
- 最多 4 个并行操作、每图最多 4 MiB；手动清理与下载互斥，清理重置失败等待。
- WinUI 仅在图像控件加载时读取；离页／控件卸载取消请求并释放位图，复用控件前检查上下文。解码宽度 96，不持有全尺寸角色图片列表。
- 设置页原有“清除图标缓存”同时清除抽卡图标及挑战资源，不删除观察、修订、账号、档案或元数据。图片不可用仍保留文字。
- 显式合成宿主只读取已缓存图片，不默认联网；生产宿主按上述公共地址加载。缓存关闭时取消并等待现有操作结束。
- 共用下载后端不再把异步续接投递回调用者 UI 上下文，避免图片 I/O 依赖 UI 消息循环；有专门回归测试。

真实 C# 缓存探测取得草史莱姆、提纳里和芙宁娜三张公共 PNG，每张最终成功探测一次请求；重复与离线读取没有新增请求。实际 WinUI 用合成挑战观察及精确匹配的公共图片显示，不再复用同一张角色图冒称其他角色或敌人。窗口中的官方来源／完成时间均为明确标注的合成 fixture，不是用户账号真实记录。

图片证据在 E:\Workspace\FurinaWorkspace\.validation\challenge-artwork-20261119，包括地址、SHA-256、字节数、C# 实际缓存结果、最终 UIA、截图和清理前后事实哈希。已核验 3 张具体图片，不据此宣称所有怪物／角色资源均已逐项验证。早期“图片端点超时、没有真实图片证据”的限制已被本节的具体成功样本更新；旧探测失败记录仍保留。

## 实现位置与验证

- Core/Metadata/AbyssScheduleMetadata.cs：平台无关公开配置值。
- Infrastructure/Metadata/SnapMetadataClient.Abyss.cs：精确关联、兼容映射及缺失处理。
- Presentation/History/HistoryDisplayModels.Abyss.cs：读取投影、分组、来源标记。
- SnapMetadataClientTests.Abyss.cs、AbyssConfigurationViewTests.cs：离线、旧结构、缺失、歧义、Int64、时间／事实不变及资源路径边界。

本机只读复制胡桃公共 CHS 元数据，离线 C# 解析 113 期；独立读取原始 JSON 验证聚合：452 层配置、1,356 间、5,155 波、12,048 敌人条目、142 机关、22 条分半异常、3,699 词缀。解析网络请求为 0。3 条名称无法解析时保留 ID；这里的计数按各期引用展开，不是去重资源数。

Windows 隔离合成库实际 UIA 验证分半异常、机关、波次、词缀、30 亿数量、旧列表、缺失及空配置；没有改写用户库、凭据或原始公共元数据。证据保存在 E:\Workspace\FurinaWorkspace\.validation\winui-abyss-config-76a88075。设备／真实接口采集与真实图片不由该验证替代。


## 剧诗卡牌／祝福观察展示（2026-11-19）

与上述深渊公共期次配置不同，卡牌／祝福来自本次官方响应，现作为 TheaterRoundDetails 的可选结构化内容保存；来源 ID、类别、描述、图标、强化状态、等级及效果均随观察贯通保存、判重、分类导出、Archive 和恢复摘要。没有来源 ID 的条目保持为空，不按名称去重。具体兼容范围见挑战历史 2.3／Archive 必需子数据集 1.4 规范；此前“格式没有因公共配置展示改变”仅描述原先的深渊元数据阶段，不适用于本节新增观察事实字段。

WinUI 每条采用图标＋名称／等级／强化标记＋完整说明；图标共用既有 FileChallengeArtworkCache 与加载／卸载取消。HoYoDescriptionText 只在显示层消除已知样式、转换换行，不修改观察；旧兼容文本仍可查看／导出。

定向及全量自动化覆盖旧 Round JSON 不变、缺失／非法值、同名不同来源、相邻判重、重新打开、Portable 应用／重导出、Archive Merge／Replace 与有效校验和的降级拒绝。独立 JSON Schema 校验覆盖 2.0～2.3 示例和不兼容版本。实际图片及窗口结果另见本轮对齐记录；真实账号／跨端新增字段验收仍待执行。


## 危战难度素材（2026-11-19）

旧观察的 IconUrl 字符串可能是公共 HTTPS URL，也可能是 UI_LeyLineChallenge_Medal_* 源资源名或逗号列表。StygianDifficultyArtwork 只解析明确源值：已知资源名 0～7 对应内置同名通用原神素材；列表使用明确末项；受信 URL 继续使用统一缓存。未知／缺失／无成绩时不从 Difficulty 数值猜图。单人和联机的源值独立，展示不回写事实，不新增格式或 Schema。

图片放在 App/Resources/Images/ChallengeDifficulty（Android 合法的小写文件名），WinUI 以 Content 链接到 Assets/ChallengeDifficulty。通过既有 ChallengePage.Artwork 处理位图及卸载，内置分支不占 HTTP 图片缓存。源素材与 WinUI Debug／Release 输出的八份 PNG 哈希一致。实际窗口验证徽章 4／5、未知回退、四次进入和来源事实不变；Android 构建包包含八份资源，真机尚未验证。

未知发生时间的列表文字通过 ListTimeText 展示已有观察／采集时刻并保留“发生时间未知”；OccurredText、领域时间和排序不变。剧诗难度显示已知枚举名称及原代码，未知代码仍明确标注。完整证据与缺口见项目对齐核对表。

## 挑战说明的统一文本投影（2026-11-19）

深渊祝福、楼层／分半异常、机关、波次说明、配置敌人名称／词缀和危战敌人说明／标签统一复用 HoYoDescriptionText.Plain，和剧诗说明保持同一展示规则。只处理已知 color／size／b／i、br、来源转义换行及 HTML 实体；未知标记保留。不得把转换结果回写元数据、观察或 Portable，更不能因文字相同合并身份。

生产解析器与实际展示构建器离线核验公开 113 期、452 层、31,627 条展示文本；按期次展开的 775 处样式波次说明全部可见。源文件哈希、解析元数据与合成观察均不变。实际 WinUI 检查换行、词缀、机关及未知标记，七个身份／事实／修订表不变。新增两项回归，全量 Release 1269/1269。证据：E:/Workspace/FurinaWorkspace/.validation/challenge-text-20261119/REPORT.md。

## 2026-11-20 Windows 展示更新

后续维护者图示要求已改为两列期次／快照、深渊逐层选项卡和同间左右半场，统计侧头像移入紧凑弹窗；危战和剧诗采用专用布局。Windows 不再逐图片订阅加载事件，改用有界异步绑定状态。图片缓存的既有安全、过期、失败旧图及清理规则保留。

富文本更新取代上节“显示时消除样式”的 Windows 结论：读取元数据保留源颜色／强调，安全解析到原生 Run；纯文本宿主仍使用文本投影。已支持原神 `{LINK#N211101}电涌{/LINK}` 术语标记、下划线及嵌套格式，源文本不回写。详见 [Windows 周期挑战展示](WINUI_CHALLENGE_PRESENTATION.md)。不新增 Schema 或公开格式。
