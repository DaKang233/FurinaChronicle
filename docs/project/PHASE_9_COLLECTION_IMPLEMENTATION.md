# Phase 9.0／9.1 采集契约与隔离验证

2026-11-10。基线为 `master @ 081df23`，Schema 12，版本 `0.8.16 / 8C.7`。相对于筹备计划的 `50f13e8`，新增提交为备选图像资源。状态为 P0 字段与验证契约已建立、授权响应验证待执行，P1 隔离工程纵切可验证。

本轮沿用 [路线图](ROADMAP.md)、[身份模型](../architecture/IDENTITY_MODEL.md)、[时间与来源](../architecture/TIME_AND_PROVENANCE.md)、[ADR 0016](../decisions/0016-character-weapon-observations-and-acquisitions.md)、[ADR 0017](../decisions/0017-challenge-observation-history.md) 和 [ADR 0019](../decisions/0019-spiral-abyss-chronicle-completion.md)。保留 Phase 8 的设备验收与 Schema 证据边界。

## 能力和证据矩阵

| 能力 | 目前证据 | 映射与首发边界 |
| --- | --- | --- |
| 国服账号和角色 | FurinaChronicle 已有 `IMiHoYoAccountProfileClient`；隔离测试使用合成角色绑定。 | 请求固定通行证、档案和 `GameAccountId`。按 `GameBiz + Region/Server + UID` 匹配且只接受一个候选。提交事务再次检查副本所属档案与共享身份。 |
| 当前期深境螺旋 | Snap.Hutao.Remastered `GameRecordClient.GetSpiralAbyssAsync` 有独立战绩请求。既有 Hutao 导出样本的字段结构与参考 DTO 一致；导出文件本身使用另一层封装。合成 HTTP 到 SQLite 已验证。 | `schedule_id` 是期次。正值 Unix 秒的 `start_time/end_time` 是可空周期。`floors/index`、`levels/index`、`star` 分别映射楼层、间和星数。响应如带 `data.uid` 则核对目标。缺期次或无效结构拒绝，明确空楼层返回 Empty。正星结果作为已形成结果的证据。零星且未核实战斗证据的格子暂不构造 Chamber 结果，防止派生出虚假的完成节点。当前期能力与前期可访问性分开验收。 |
| 深渊实际完成时刻 | 参考结构和导出样本包含 `battles/index/timestamp`，也存在楼层结算字段。 | 初版响应映射保留 Unknown，待授权响应证明可由最后完成的 Chamber 及其上下半得出可信完成时刻后接入。楼层结算、期末或抓取时刻不能补作完成时刻。Chronicle 规则沿用 ADR 0019。 |
| 海外深渊 | FurinaChronicle 有海外角色绑定，Snap.Hutao.Remastered 有单独的 `GameRecordClientOversea`。 | 已在隔离入口接入 HoYoLAB 传输并通过合成验证。海外凭据、权限、期次和响应仍需使用真实账号核验。 |
| 角色与随身装备 | Snap.Hutao.Remastered 角色列表及详情 DTO 有角色、武器、命座字段。武器 DTO 的 `id` 使用 `WeaponId`，当前参考未建立实例身份。 | P2 候选映射为角色类型 ID、等级、可证明的已激活命座，以及已检查角色所展示的武器类型和数量。覆盖范围单独声明。若只有类型 ID，按类型汇总，等级或精炼分布若无法无损表达则保持未知。此路径无法证明完整背包。 |
| 失败与限流 | 合成 HTTP 覆盖 401、403、429、503、错误 JSON、非零业务码、超限和取消。 | 已识别 HTTP 失败类别和 `Retry-After`。未知业务码统一拒绝，真实业务码、通行证查询错误脱敏和有界重试留给经授权验证的传输层。 |

可核对的本地参考位置为 Snap.Hutao.Remastered 的 `Web/Hoyolab/Takumi/GameRecord` 下的 `GameRecordClient.cs`、`GameRecordClientOversea.cs`、`SpiralAbyss/` 和 `Avatar/`。读取既有导出样本时只对照字段名，真实标识和历史内容不进入仓库。

## 隔离纵切行为

- `CollectSpiralAbyss` 通过 `ISpiralAbyssSource` 接收当前期响应，经 `IArchiveDomainHistoryStore` 写入。`MiYouSheSpiralAbyssResponseReader` 负责有界读取和规范映射。测试用假 HTTP handler 接入 Reader，命令见下。
- 只保存规范化事实与来源。`FetchedAt` 是响应正文读完的 UTC 时刻，`ObservedAt` 在这里表示应用于该时刻获得状态证据。`ImportedAt` 留空，`SourceRecordId` 留空。期次 ID 无法作为不可变观察 ID。每次尝试有新的写入操作 ID，保存的观察带独立采集批次。
- 期次和楼层采用 Partial，只表示收到并成功映射的楼层、间和星数。缺可靠完成证据时保留 Unknown。2 MiB 为隔离 Reader 的响应上限。D09 下暂不落盘原始响应，失败结果不回传服务端正文。
- 相邻相同内容、同刻冲突和 A→B→A 由现有存储规则处理。合成测试确认另一个档案里的同角色副本保持独立。
- 当前挑战 Tombstone 按具体观察 ID 建立。无稳定来源观察 ID 的刷新若生成新 ID，单条 Tombstone 匹配不足以防止原样重采。隔离用例因此在提交事务内遇到本档案该共享角色的活动挑战 Tombstone 时拒绝刷新，并保留原标记。此保守门槛暂不区分同一期的旧观察和新观察。其他档案不受影响。正式开放前应以授权样本确认能否可靠识别原观察，或为缺稳定 ID 的情况评审正式产品行为。
- 本轮将操作保持在测试隔离入口。实际国服战绩请求适配器、凭据获取、真实业务错误分类和设备入口仍待实现及验证。版本、Schema、Portable 和 Archive 格式均沿用现有值。

## 下一步验证

1. 用维护者授权的国服测试账号和隔离库，先确认现有角色绑定能唯一匹配预期区服。取得本期正常、明确无记录或未解锁、登录失效／权限限制三类可复现样本。只记录脱敏字段、业务错误类别和可访问范围。
2. 确认战绩传输所需的凭据类型与正常验证流程。将参考 DTO 与实际结构逐项对照，优先确认当前期 ID、完成时间的证据和业务限流。没有权限或需要交互验证时返回相应状态。
3. 将正常与失败样本缩减为合成回归。完成正式采集传输后，在隔离入口验证保存、重开、重复刷新及断网查看，再按计划进入角色／随身装备纵切和双端手动入口。
4. 在生产入口开放前完成删除抑制评审、错误脱敏与设备验收。Windows 和 Android 应用构建、真机与授权接口均为 Not run。

```powershell
dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SpiralAbyssCollectionTests
```

## 实际验证

- Restore 成功，最终 Release 全量测试 667／667 通过，0 失败、0 跳过，其中本轮新增 15 项。
- `git diff --check` 和仓库版权头检查通过。新用例通过合成 HTTP、临时 SQLite、数据库重开和隔离档案验证。

### 2026-11-11 授权角色样本

- 通过本机 Snap Hutao Remastered 2.0.1.0 打开授权角色的深境螺旋页并触发「刷新数据」。界面显示本期第 124 期，刷新后未取得可核验的新响应或成功提示，因此在线请求仍为待验证。
- 以只读方式查询本机胡桃数据库中的单一授权角色缓存，将挑战正文交给实际 FurinaChronicle 响应解析器。26 个期次全部通过，覆盖 58 层、174 个 Chamber 和 26 个有效周期。第 124 期解析为 2 层、6 间、18 星。缓存正文通过临时标准响应封装送入解析器，验证范围为正文映射。
- 174 个 Chamber 均有两半的正值战斗时间戳，其中包括 26 个 12-3。已验证字段存在性，完成时刻语义仍待来源确认，解析结果保持 Unknown。仓库记录只包含汇总。

### 2026-11-11 国服在线验证

国服战绩传输已接入当前期深渊采集。隔离命令以米游社扫码取得当次登录态，在临时档案确认角色归属并调用采集用例。成功时依次验证首次提交、重复提交及数据库重开。诊断保留状态类别、HTTP 状态与上游数字错误码。

```powershell
dotnet run --project eng/phase9/FurinaChronicle.Phase9Probe -c Release -- <国服游戏UID>
```

使用授权角色两次扫码。角色绑定通过，第一次收到服务拒绝。补齐战绩请求头并增加脱敏诊断后，上游返回业务码 `5003`，分类为访问受限。未写入观察，实时成功响应、重复提交和重开仍待验证。若角色在米游社官方战绩页恢复可查，可以重跑同一命令。

FufuLauncher 的参考实现位于 `Services/CycleChallenge/GameRecordApiClient.cs`、`CycleChallengeService.cs` 和 `Views/Pages/CycleChallengePage.xaml`。它提供国服战绩接口、未参与与未解锁的状态区分，以及当前期、前一期和本地历史的页面组织。此轮补充未解锁、未参与和业务限流的状态处理。后续展示可沿用账号／角色选择、挑战类型标签、期次列表和分模式详情的组织。FufuLauncher 单独展示上下半的时间，本轮继续保留 FurinaChronicle 的完成时间证据边界。
隔离命令 Release 构建和全量 679 项测试通过。回归覆盖上游错误码、请求参数和未解锁／访问受限时跳过数据库写入。

## 2026-11-11 Phase 9 与 Phase 10 联合验证入口

- 国服源接受当前期与前一期，按所选期次请求并以响应中的官方期次标识写入。命令行支持可选的 `current` 或 `previous` 参数。
- MAUI 主页进入「挑战历史」。档案和账号选择独立于用户页，按账号副本分页读取已保存的深境螺旋观察。每条卡片显示期次、观察时间、覆盖范围、楼层与间的星数。
- 选择采集期次并点击「隔离验证采集」会使用用户页已选的米游社通行证。验证会核对角色绑定，将首次结果写入临时库，再验证同一期重复提交和重开读回。界面报告验证结果，临时库和已保存档案的数据归属独立。
- Release 测试 681 项通过。命令行、Windows x64 MAUI、Android ARM64 MAUI 的 Release 构建通过。Windows 用两个同 UID 的合成档案实际验证了导航、21 条数据翻页、跨档案切换和未登录时保留当前列表。Android 真机交互未运行。
- 实时成功响应仍待验收。上次授权角色的上游结果为 `5003`。官方战绩可访问时，可从用户页选中该角色所属通行证，再由挑战历史页对相应的档案账号执行验证。

### 国服战绩人机验证

维护者确认 `5003` 表示需要风控验证。采集源将 `5003` 和 `1034` 归类为需验证，挑战历史页使用现有 GeeTest 页面让用户完成挑战。米游社确认验证后，采集源以服务端签发的结果重试原账号和原期次一次。取消、验证失败或重试仍被要求验证时，界面显示对应状态。验证材料仅用于当次请求，命令行探针会报告需要验证，交互验收通过 MAUI 挑战历史页执行。

（2026-11-11 更正：`1034` 才是卡片极验的触发码；`5003` 是账号级风控，正确做法是先请求 `index` 端点并在该路径上完成验证。`5003` 不再归类为需验证，见文末修复记录。）

复核 Snap.Hutao.Remastered 的国服战绩实现后，确认它把 GeeTest 结果交给战绩服务端换取当次挑战证明，重建并签名原请求，然后立即重试。后续普通刷新使用原账号会话，是否再次要求验证由服务端决定。胡桃此处的自动重试条件是 `1034`。FurinaChronicle 另根据维护者实测处理 `5003`。

界面分开显示申请挑战、提交验证和验证后刷新，使用相应请求的 HTTP 状态或业务码。隔离验收的第二次请求观察当前会话是否已经可直接刷新，首次采集成功时保留其结果。国服战绩和 HoYoLAB 登录可复用同一人工 GeeTest 页面，各自的服务端验证及重试仍由对应请求处理。

### 提交验证返回 10306

维护者实测在胡桃完成验证后，胡桃和 Starward 可继续普通刷新。Starward 的国服深渊调用直接发送战绩请求。FufuLauncher 在该情况下尝试刷新 Cookie 后仍失败。该组合支持把账号会话是否可直接读取与单次验证提交是否成功分开观察。

WebView 将 GeeTest 的 `challenge`、`validate`、`seccode` 原样传给客户端，提交使用 SDK 返回的 `seccode`。提交返回 `10306` 时用同一账号会话检查一次普通战绩请求。服务端允许读取时正常返回战绩，仍受限时保留提交阶段的 `10306`。自动测试覆盖两条路径。米游社未给出本次 `10306` 的确定原因，真实账号仍需复验。

再次真实复验仍返回 `10306`。核对胡桃的 `CardClient` 与 HTTP 客户端注册后，确认其验证码申请与提交共用启用 Cookie 的客户端，深渊普通请求则使用独立客户端。FurinaChronicle 现在把申请响应中的有效会话 Cookie 传给同一次验证提交。账号 Cookie 优先使用所选通行证，临时 Cookie 的生命周期限于单次验证，且按请求域和路径约束。

失败提示记录提交时是否携带验证会话 Cookie。自动测试覆盖服务端下发会话、未下发会话、不匹配路径及试图覆盖账号 Cookie 的情况。米游社是否对真实账号的此次请求下发验证会话及其是否影响 `10306`，仍需通过新构建复验。

（2026-11-11 真实请求复核更正：申请响应只下发 `acw_tc`、`aliyungf_tc` 等 WAF Cookie 并原样回显 `ltuid`／`ltoken`，没有验证会话 Cookie。此前「已携带当次验证会话」的结论来自把 WAF Cookie 计为验证会话，见文末复核记录。）

### 同一登录态的 Windows 真实调试

使用本机已登录账号实际执行两轮人工验证。两轮初始战绩请求均为 HTTP 200／业务码 `5003`，申请挑战均为 HTTP 200／业务码 0。网页回调的挑战与申请一致，返回了完整的三项 GeeTest 结果，选定会话具备 LToken 和 CookieToken，未保存设备指纹。携带当次会话 Cookie 的提交仍返回 HTTP 200／业务码 `10306`。

Snap.Hutao.Remastered 的 `ApiEndpoints.csv` 表明 `GameRecordSpiralAbyssPath()` 返回完整 URL。FurinaChronicle 已据此修正申请和提交的挑战作用域，回归测试要求两处均发送完整 URL。真实复测确认此修正后仍返回 `10306`。目前无法从这一结果确定服务端拒绝的原因，实时采集仍未通过。临时调试日志只包含状态和字段存在性，调试代码已移除。

### 胡桃未打包开发版对照

从 Snap.Hutao.Remastered `3b7475ed3` 构建 Windows x64 Debug，直接启动未打包程序。调试副本使用独立的设置、数据及缓存目录，扫码登录后观察到两次深境螺旋请求的初次及最终业务码均为 0，用户会话均有设备指纹。已核对该请求通过 `SetUserCookieAndFpHeader` 写入相应请求头。两次请求都未进入验证分支，因此没有取得胡桃在本次环境下的提交结果。两个客户端使用各自建立的会话，本次观察无法将 `10306` 归因于设备指纹或其他单一变量。

### 米游社和 HoYoLAB 深渊战绩采集

挑战历史的隔离入口按所选通行证和角色区服路由国服／海外请求，继续核对唯一角色绑定。HoYoLAB 当前期和前一期使用海外战绩地址及对应会话 Cookie，观察来源标为 `hoyolab`。国服继续记录为 `miyoushe` 并支持已有人工 GeeTest 流程。海外服务若返回验证要求，显示其状态，避免向国服验证端点发送海外会话。

合成测试覆盖海外请求及期次、拒绝跨区服请求、登录失效、验证要求、观察写入、重复采集和 SQLite 重开。Release 测试 691 项通过，Windows x64 Debug、Android ARM64 Debug 和国服命令行探针构建通过。Android 真机未运行。

Windows 本期国服在线复测再次进入人工验证，提交结果为业务码 `10306`，界面确认已携带当次验证会话。未取得新观察，未写入业务数据库。HoYoLAB 在线验证待提供可用的海外会话及对应角色。

### 米游社登录中的设备指纹

扫码确认和短信登录成功后，如果实际登录端点的响应带有单个且格式合法的 `x-rpc-device_fp`，则与本次登录所用的设备 ID 一同保存到通行证安全存储。存在指纹的设备上下文发生变更时，使用当前设备记录，并从本次登录重新取得缺少的衍生凭据。战绩请求使用所选通行证中保存的设备信息。

自动测试覆盖扫码和短信响应、缺失或非法值、已有账号重新登录时的设备配对，以及战绩请求头。响应头处理以合成测试验证。目前尚未在真实扫码或短信响应中确认该响应头，也未据此重新验证 `10306`。

（2026-11-11 复核：已用真实扫码会话确认战绩请求不带 `x-rpc-device_fp`，见文末。）

### 2026-11-11 `10306` 复核、诊断与修复

**基线**：`master`、HEAD `4c6917ac`（本机 `origin/master` 落后两个提交），工作区保留原有未提交与未跟踪改动。维护者观察 `10306` 的实际运行构建是 MAUI `FurinaChronicle.App` 的 Windows Debug 产物，构建时间 2026-11-11 21:32:42，晚于全部相关源码修改时间且二进制含挑战历史／验证流程字符串；`FurinaChronicle.Windows` 是抽卡只读预览宿主，不含该链路。授权角色 `274079782`（`cn_gf01`，当前期）。

**诊断方法**：临时诊断仅在 `FURINA_DIAG_FILE` 设值时写入，内容限于方法、主机、路径、查询串、请求头名、Cookie 名、`Set-Cookie` 名、字段存在性与长度、HTTP 状态、业务码、上游 `message`、耗时。不记录 Cookie 值、Token、设备指纹、GeeTest 原始值和签名。复跑结束后诊断文件已删除、代码已移除。

**真实请求序列（三轮，均为人工完成极验后提交）**：

| 阶段 | 请求 | 结果 |
| --- | --- | --- |
| 通行证会话 | 扫码登录后的所选账号 | 有 SToken／LToken／CookieToken；设备 ID 为 36 字符 GUID；无设备指纹 |
| 普通战绩请求 | `GET …/genshin/api/spiralAbyss?role_id=274079782&schedule_type=1&server=cn_gf01` | HTTP 200／业务码 `5003`，`message` 为空；`Set-Cookie` 名 `acw_tc`、`aliyungf_tc`、`ltoken`、`ltuid` |
| 申请挑战 | `GET …/card/wapi/createVerification?is_high=true` | HTTP 200／业务码 0，`gt` 32 字符、`challenge` 32 字符、`success` 1、`new_captcha` 1 |
| 提交验证 | `POST …/card/wapi/verifyVerification` | HTTP 200／业务码 `10306`，`message` 为空 |
| 验证后刷新 | 普通战绩请求 | HTTP 200／业务码 `5003` |

- 三轮的客户端变量分别为：① 原实现（BBS 版本 2.95.1、旧 Cookie 拼接）；② 仅把 BBS 版本对齐到本机参考构建实际发送的 2.115.0；③ 在②基础上让申请响应新下发的 Cookie 覆盖账号 Cookie。三轮回合与刷新的业务码完全相同，说明版本与 Cookie 覆盖都不是 `10306` 的成因；②已回退，避免无证据的行为变更。
- 申请响应没有下发验证会话 Cookie：`acw_tc`／`aliyungf_tc` 是响应头层的 WAF Cookie（首个战绩响应同样下发），`ltoken`／`ltuid` 是被原样回显且值与账号一致。提交实际携带的「会话」只有这两个 WAF Cookie。
- `10306` 响应没有 `x-rpc-aigis`、`x-rpc-verify` 等可继续处理的头，HTTP 为 1.1，响应仅含 `X-Trace-Id` 等常规头；业务码与 `message` 都不足以在客户端区分「验证失败」「风控拒绝」「挑战过期」。
- 客户端请求形状（URL、query、`x-rpc-challenge_game`、完整 URL 的 `x-rpc-challenge_path`、DS gen2 与 salt、body 字段与 `seccode` 形态、Cookie 集合）与参考实现逐项一致，唯一实质差异是不发送 `x-rpc-device_fp`。参考的三个客户端都通过 `public-data-api.mihoyo.com/device-fp/api/getFp` 申请指纹，且构造参数是虚构的 Android 设备画像；按本任务「只使用自身会话和真实设备信息，禁止模拟设备」的约束，未移植该实现。

**本轮实施的最小修复**：

1. 申请响应新下发或轮换的 Cookie 在提交时覆盖账号 Cookie（与参考客户端共享 Cookie 容器的语义一致）。此前会丢弃轮换后的 `ltoken` 等值；本轮服务端回显值与账号一致，因此没有改变实际提交内容，但消除了一个真实分歧。
2. 判定「提交是否携带验证会话 Cookie」时排除 `acw_tc`／`aliyungf_tc` 等 WAF Cookie，只统计新下发或值发生变化的 Cookie。此前会把 WAF Cookie 报成验证会话，误导界面与记录。
3. `eng/phase9/FurinaChronicle.Phase9Probe` 不再丢弃登录返回的 `tokens.DeviceFingerprint`，避免命令行探针与应用行为分叉。

未改动数据库 Schema、Portable／Archive 格式、生产采集入口或已接受的数据语义，因此不需要新 ADR。

**验证**：`FurinaChronicle.Tests` Release 全量 701 项通过（其中 `HoYoGameRecordSpiralAbyssSourceTests` 24 项，新增 WAF-only Cookie 不计为验证会话的回归）；Windows x64 MAUI Debug 构建通过；Phase 9 命令行探针 Release 构建通过；`git diff --check` 与版权头检查通过。

**在线采集结论：未通过**。服务端在人工验证提交阶段连续三轮返回 `10306`，没有可解析的深渊数据，业务数据库与隔离库都没有写入。最终构建的提交请求在第①轮基础上只多出两项非请求性修正（BBS 版本已回退、会话 Cookie 判定不参与请求构造），其方法、URL、头集合、Cookie 名与值、body 与第①轮一致（DS 由时间戳与随机数生成，每次请求不同），因此没有再消耗一次人工验证去重复已知结论。

（22:47 复核更新：参考客户端完成账号级验证后，同一构建、同一账号的隔离采集已经通过，见下一节。该节的"未通过"只描述本客户端自行触发并提交验证时的结果。）

**仍需确认的假设**：

- H1：服务端要求请求具备已注册的设备指纹。该假设无法在本任务约束内实现（需要虚构设备画像），且具备指纹的 FufuLauncher 在同一场景下同样失败，因此不能据此继续缩小。
- H2：`5003` 是账号级风险提示，`createVerification`／`verifyVerification` 未必是清除它的通道——参考实现只在 `1034` 上自动进入该流程，另一参考客户端在 `5003` 时提示用户到官方网页完成验证。若能在该账号上取得参考客户端真正进入验证分支时的原始请求，才能确认。
- H3：账号或当前会话本身处于风控中，只有官方 App／网页的验证才会解除。

**维护者后续可选步骤**：在米游社 App 或官方战绩网页上对 `274079782` 完成一次安全验证后重跑「挑战历史 → 隔离验证采集」；或在胡桃真正进入验证分支时抓取一次 `createVerification`／`verifyVerification` 的脱敏请求；或改在 Android 真机上验证。三项任一完成前，国服在线采集保持未通过。

### 2026-11-11 胡桃实况对照：申请、获取与导入

**目的与约束**：前几节只有 FurinaChronicle 的失败样本，参考客户端"能成功"只有间接记录。为取得同账号、同机、同一时刻的完整请求证据，在独立副本 `J:\DSH-Temp\hutao-phase9-e5ef2c65`（未打包、独立数据目录，非只读参考仓库）加入临时脱敏跟踪后实际运行。只观察胡桃自身会话，不移植其指纹、凭据或验证材料；`E:\Users\14160\source\repos\Snap.Hutao.Remastered` 保持只读。

**临时调试代码**（仅存在于该副本，未进入本仓库）：

- `Core/Diagnostics/Phase9FlowTrace.cs`：JSONL 追加到可执行文件旁的 `phase9-flow.jsonl`，只记录方法、主机、路径、查询串、请求头名、关键头的存在性与长度、Cookie 名、`Set-Cookie` 名、HTTP 状态、业务码、`message`、数据摘要（期次／星数／楼层数等）。不记录 Cookie 值、Token、指纹、GeeTest 值、签名。
- `Core/Diagnostics/Phase9FlowTraceHandler.cs`：`DelegatingHandler`，在 `ConfigureHttpClientDefaults` 链尾注册，捕获最终发出的请求；对 `spiralAbyss`、`card/wapi/createVerification`、`verifyVerification`、`device-fp/getFp`、`genshin/api/index` 生效。
- `Service/SpiralAbyss/SpiralAbyssService.cs`：导入完成后记录 `uid`、期次、星数、层／间数、动作（add／update）、实体 ID 与数据库路径。
- 既有隔离补丁：数据目录 `exe\HutaoTraceData`、缓存 `HutaoTraceCache`、设置 `HutaoTraceSettings`；关闭 Sentry 遥测与协议注册；`GeetestService` 去掉自定义自动解题，仅保留 WebView2 人工验证。

**实测链路**（2026-11-11 22:45，UID `274079782`，`cn_gf01`，人工完成极验）：

| 步骤 | 请求 | 结果 |
| --- | --- | --- |
| 1 角色资料 | `GET /game_record/app/genshin/api/index?role_id=…&server=cn_gf01` | HTTP 200／业务码 **`1034`**，无 `data` |
| 2 申请挑战 | `GET /game_record/app/card/wapi/createVerification?is_high=true`，`x-rpc-challenge_game: 2`，`x-rpc-challenge_path: …/genshin/api/index` | HTTP 200／`0`，`gt`／`challenge` 各 32 字符 |
| 3 人工极验 | WebView2 | 约 11 秒完成 |
| 4 提交验证 | `POST /game_record/app/card/wapi/verifyVerification` | HTTP 200／**`0`**，`data.challenge` 32 字符 |
| 5 验证后刷新 | `GET …/genshin/api/index`，带 `x-rpc-challenge` | HTTP 200／`0` |
| 6 获取前期 | `GET …/spiralAbyss?schedule_type=2&role_id=…&server=…` | HTTP 200／`0`，`schedule_id` 123，`total_star` 36，`max_floor` 12-3，2 层／6 间／12 战斗 |
| 7 获取本期 | 同上，`schedule_type=1` | HTTP 200／`0`，`schedule_id` 124，`total_star` 36，2 层／6 间／12 战斗 |
| 8 导入 | EF 表 `spiral_abysses` | 123、124 两行，`Uid`＋`ScheduleId`＋原始 snake_case JSON（约 27.7KB），动作为 update |

请求头对照：两处请求均带 `x-rpc-app_version: 2.115.0`、`x-rpc-client_type: 5`、`x-rpc-device_id`、`x-rpc-device_fp`（13 字符，存在但不记录值）、`DS`；Cookie 名为 `account_id`／`cookie_token`／`ltoken`／`ltuid`。`spiralAbyss` 请求本身不带 `x-rpc-challenge`；只有验证后的 `index` 重发带 32 字符 `x-rpc-challenge`。

导入后读回：`spiral_abysses` 中 `ScheduleId=123/124`、`Uid=274079782`，JSON 保留服务端原始字段名与完整 `floors`／`levels`／`battles`。服务端 `total_star=36` 与已展示楼层星数之和（11、12 层各 9）不同，说明该字段是服务端聚合值，不能由楼层星数推导；FurinaChronicle 的 18 星来自 Chamber 星数映射，属不同口径，不构成错误。

**关键结论**：

- 胡桃在当前风控状态下由 `index` 端点先拿到 `1034`，并把 `x-rpc-challenge_path` 指向 **index**；FurinaChronicle 直接请求 `spiralAbyss` 得到的是 **`5003`**，并把挑战绑定到 `spiralAbyss`。两者触发码和挑战路径不同。
- 胡桃对 `1034` 的提交返回 `0`；FurinaChronicle 对 `5003` 的提交返回 `10306`（`message` 为空）。即 `5003` 不是"卡片极验"的有效触发条件——申请挑战总能成功，但提交会被拒。
- 胡桃完成验证后，**未做任何改动的 FurinaChronicle 构建**（无设备指纹、BBS 版本 2.95.1）在同一账号上直接读到战绩，隔离采集显示"隔离采集已写入 · 后续刷新无变化 · 重开读回 1 条 · 第 124 期 · 18 星"。首次写入、重复采集 NoOp、重开读回三项同时成立。
- 因此排除设备指纹、BBS 版本、Cookie 集合／顺序、DS、完整 URL 形式的 `challenge_path`、请求头集合作为 `10306` 的原因。剩余失败点只在"FurinaChronicle 自己选择并完成了错误的验证触发路径"。

**后续修复（2026-11-11 已实施）**：`5003` 不再直接走卡片极验，而是先请求 `index`；若 `index` 返回 `1034`，用 `challenge_path=index` 走现有申请／人工验证／提交流程，带 `x-rpc-challenge` 重发 `index`，再重发 `spiralAbyss`；若 `index` 不是 `1034`，返回账号级风控状态并停止，不再弹验证。

实施细节：

- `MiYouSheSpiralAbyssResponseReader`：`1034` 归为 `VerificationRequired`，`5003` 改为 `AccessDenied`（此前把 `5003` 当作需验证是 `10306` 的直接来源）。
- `HoYoGameRecordSpiralAbyssSource`：新增 `index` 探测与 index 作用域的申请／提交；挑战头 `x-rpc-challenge_path` 对 `index` 和 `spiralAbyss` 分别使用各自完整 URL，`x-rpc-challenge_game` 仍为 `2`；验证成功后先带 `x-rpc-challenge` 重发 `index`，再请求战绩。探测不是 `1034` 时不弹验证、不消耗人工验证。
- 回归测试：新增「5003 → index 1034 → index 作用域验证 → 战绩」成功路径、「index 非 1034 → AccessDenied 且不提示」、取消路径；原 `1034` 分支与 `10306` 回退用例保持并通过。Release 全量 704 项通过。
- 构建：Windows x64 MAUI Debug、VS 2026 MSBuild 全解决方案 Debug（App 的 android／windows 两个 TFM 与 Windows 项目）、Phase 9 探针 Release 均通过；`git diff --check` 与版权头检查通过。

**待复验**：当前账号在参考客户端验证后处于可读状态，`5003` 分支未能现场触发，因此新路由只由合成测试覆盖；下一次出现账号级风控时，需要真实执行一次 `5003 → index 1034 → 人工验证 → 战绩写入` 才能把该修复标为在线通过。

**构建环境排障（2026-11-11）**：VS 2026 的还原跑在 .NET Framework NuGet 栈上，经本机 `127.0.0.1:9000` 代理访问 HTTPS 源时报 `ServicePointManager 不支持具有 https 方案的代理`。一旦还原失败，`FurinaChronicle.App/obj/project.assets.json` 会被写成缺少 `net10.0-android`／`net10.0-windows10.0.19041.0` 目标的版本，后续构建报 `NETSDK1005`／`NETSDK1004`（`-f` 单目标构建也会留下同类资产，可用于复现）。恢复方式：执行 `dotnet restore FurinaChronicle.slnx`（或用 VS 的「还原 NuGet 包」）生成完整资产，关闭残留 MSBuild/dotnet 节点后重新构建；已验证 VS 2026 MSBuild 全解决方案 Debug（App 两个 TFM 与 Windows 项目）构建通过。这是本机代理与还原缓存的状态问题，不是仓库配置缺陷。

### 2026-11-11 夜间复核：`5003` 的设备信任根因

上一节把 `5003` 当作“验证触发码”的修复并未让线上恢复。继续用同机参考客户端做对照后，根因收敛为**设备信任**，而不是请求构造。

**实施的客户端改动**：

- 新增 `IDeviceFingerprintProvider` 与 `MiHoYoDeviceFingerprintClient`：用官方 `https://public-data-api.mihoyo.com/device-fp/api/getFp` 为本机自己的设备 ID 注册指纹；载荷使用 `platform=2` 与 FurinaChronicle 自生成的通用设备画像（维护者明确授权；未复制任何参考客户端的指纹或凭据值）。
- 扫码／短信登录在登录响应没有指纹时主动注册并保存；`PassportCredentialMaintenanceService` 在启动维护时为缺少指纹的大陆账号按现有设备 ID 补齐（无需重新登录）。交互式登录改为采用本次会话的设备 ID，使登录、指纹注册和战绩请求使用同一设备上下文。
- 战绩采集改为**先请求 `index`、后请求 `spiralAbyss`**：`index` 返回 `1034` 时在 `challenge_path=index` 上完成申请／人工验证／提交，带 `x-rpc-challenge` 重发 `index`，再取战绩；`5003` 不再触发 abyss 作用域的极验（reader 将 `5003` 映射为 `AccessDenied`）。
- BBS 版本从过时的 `2.95.1` 对齐到本机参考构建实际发送的 `2.115.0`。
- 新增回归：指纹客户端（成功／非零码／空值／非法设备 ID／网络失败）、登录时注册指纹、维护时补齐指纹、index 优先的成功／拒绝／取消路径；Release 全量 716 项通过。

**对照实验（同一账号、同一台机器）**：

1. 胡桃自身会话，仅通过环境开关移除它自己的 `x-rpc-device_fp` 请求头 → `index` 从 `0` 变为 `5003`。证明该头是访问的必要条件。
2. 清空胡桃调试副本中的指纹后重启（隔离数据库，仅该副本）：胡桃用官方接口重新注册了一个新指纹，请求带新指纹，`index` 仍为 `5003`。证明**新注册的指纹不被信任**，注册本身不能建立信任。
3. FurinaChronicle 已完成指纹注册、设备上下文对齐、`2.115.0` 版本与 index 优先顺序：请求确认携带 `x-rpc-device_fp`（长度 13），`index` 仍为 `5003`，服务端不再提供 `1034` 验证入口。
4. 22:45 在参考客户端完成一次人工验证后，未改动的 FurinaChronicle 曾能直接读取战绩；验证通过带来的账号级窗口过后，访问再次被拒。说明信任绑定在设备／会话上，且会过期。

**结论**：`5003` 表示当前设备／账号未被信任。`x-rpc-device_fp` 是必要条件，但**客户端无法用自己新注册的指纹自举信任**；需要已受信任的设备身份，或在官方渠道完成与该设备绑定的验证。当前 FurinaChronicle 在失败时正确显示「战绩访问受限（业务码 5003）」，不进入无效的极验循环、不写库。

**后续可选路径（需维护者决定或操作）**：

- 在官方 App／网页上完成设备验证后，用「手动 Cookie 登录」导入本人会话（解析器已支持 `DEVICEFP`），让 FurinaChronicle 使用用户自己的受信任设备指纹；
- 或明确授权从本机已有的受信任参考客户端会话中复用该指纹（此前规范明确禁止移植其他客户端指纹，需要新的决定）；
- 或在该账号恢复可验证状态后重试，观察服务端是否对当前指纹下发 `1034`。

**清理与副作用**：临时诊断代码、日志与环境开关均已移除；胡桃调试副本的指纹在实验中被清空（仅该隔离副本，重启后会重新注册），其源码临时开关已还原。参考仓库 `Snap.Hutao.Remastered` 始终只读。

#### 2026-11-12 00:00 受信任指纹复用复核

维护者说明该账号关闭了新设备验证、米游社没有设备验证入口，并授权复用本机参考客户端的受信任指纹。据此补充实验：

- 从本机**已安装**的胡桃数据库只读取其指纹（13 位、当天更新），一次性导入 FurinaChronicle 当前大陆账号（账号仍使用自己的设备 ID）；同时把同一指纹写回胡桃调试副本并启动。两者请求均带该指纹，`index` 仍返回 `5003`。
- 说明此刻的拒绝**不再由指纹值或客户端实现差异解释**：同一指纹在参考客户端中也被拒。结合时间线（本轮排查前该账号可读取；本轮进行了大量失败重试、多次重新登录与多次指纹注册），最可能的原因是服务端对短时间内的异常重试/设备变动进入临时风控冷却，需要停止请求、等待冷却后再复测。
- 代码侧保持的改动：指纹注册（官方接口、`platform=2`、自生成画像）、启动维护补齐、交互登录采用会话设备 ID、**重登保留已建立的指纹**（除非登录响应显式提供），采集按 index 优先并在 `1034` 时验证、`5003` 映射 `AccessDenied`。
- 临时导入与日志钩子已移除；已导入的指纹保留在账号安全存储中。下一步只在冷却后做**一次**真实复测；若仍为 `5003`，应在同一分钟内用已安装的参考客户端做对照，再判断是账号状态还是客户端差异。

#### 2026-11-12 00:36 换账号验收：64 KB 缓冲上限缺陷与修复

旧账号 `274079782` 经确认是本机“机器＋账号”风控（胡桃内置网页服务在本机也打不开，手机端正常），与客户端无关。改用新账号 `somewhere1246`（AID `400379797`，角色 UID `291245883`）验收，胡桃上可正常刷新。

- **现象**：新账号扫码后，`index` 实际返回 `200 / retcode 0`（Reqable 抓包确认，含完整档案 JSON），但 `隔离验证采集` 显示“战绩服务暂不可用”。
- **根因**：`HoYoGameRecordSpiralAbyssSource.ReadVerificationAsync` 用 `LoadIntoBufferAsync(64 * 1024)` 缓冲响应；旧账号的 `index` 一直返回 `5003`（小响应）所以从未触发，而新账号 `index` 返回完整档案 JSON（远大于 64 KB），缓冲抛 `HttpRequestException: Cannot write more bytes to the buffer than the configured maximum buffer size: 65536.`，被映射为 `Unavailable`。
- **修复**：把该缓冲上限改为 `MaxVerificationResponseBytes = 4 MiB`（仍保留上限，超限会可预期地失败）；新增回归 `IndexProfileLargerThanLegacyBufferIsStillParsed`（用 200 KB 的 `index` 响应验证不再失败）。
- **验收结果（新账号，本机）**：`隔离验证采集` 显示 **“隔离采集已写入 · 后续刷新无变化 · 重开读回 1 条 / 第 124 期 · 34 星”**；诊断记录 `index 200/0`、`abyss 200 Received`、二次刷新 `Received`（无变化），隔离库写入、重复刷新与重开读回三项均满足。
- **收尾**：临时诊断、抓包代理均已关闭/移除；旧账号指纹实验的调试副本保留已安装客户端的指纹。Release 全量 **717** 项测试通过，App/Windows/探针与 VS 2026 全解决方案构建通过，`git diff --check` 与版权头检查通过。
