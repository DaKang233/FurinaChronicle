# Phase 9.0／9.1 采集契约与隔离验证

2026-11-10。基线为 `master @ 081df23`，Schema 12，版本 `0.8.16 / 8C.7`。相对于筹备计划的 `50f13e8`，新增提交为备选图像资源。状态为 P0 字段与验证契约已建立、授权响应验证待执行，P1 隔离工程纵切可验证。

本轮沿用 [路线图](ROADMAP.md)、[身份模型](../architecture/IDENTITY_MODEL.md)、[时间与来源](../architecture/TIME_AND_PROVENANCE.md)、[ADR 0016](../decisions/0016-character-weapon-observations-and-acquisitions.md)、[ADR 0017](../decisions/0017-challenge-observation-history.md) 和 [ADR 0019](../decisions/0019-spiral-abyss-chronicle-completion.md)。保留 Phase 8 的设备验收与 Schema 证据边界。

## 能力和证据矩阵

| 能力 | 目前证据 | 映射与首发边界 |
| --- | --- | --- |
| 国服账号和角色 | FurinaChronicle 已有 `IMiHoYoAccountProfileClient`；隔离测试使用合成角色绑定。 | 请求固定通行证、档案和 `GameAccountId`。按 `GameBiz + Region/Server + UID` 匹配且只接受一个候选。提交事务再次检查副本所属档案与共享身份。 |
| 当前期深境螺旋 | Snap.Hutao.Remastered `GameRecordClient.GetSpiralAbyssAsync` 有独立战绩请求。既有 Hutao 导出样本的字段结构与参考 DTO 一致；导出文件本身使用另一层封装。合成 HTTP 到 SQLite 已验证。 | `schedule_id` 是期次。正值 Unix 秒的 `start_time/end_time` 是可空周期。`floors/index`、`levels/index`、`star` 分别映射楼层、间和星数。响应如带 `data.uid` 则核对目标。缺期次或无效结构拒绝，明确空楼层返回 Empty。正星结果作为已形成结果的证据。零星且未核实战斗证据的格子暂不构造 Chamber 结果，防止派生出虚假的完成节点。当前期能力与前期可访问性分开验收。 |
| 深渊实际完成时刻 | 参考结构和导出样本包含 `battles/index/timestamp`，也存在楼层结算字段。 | 初版响应映射保留 Unknown，待授权响应证明可由最后完成的 Chamber 及其上下半得出可信完成时刻后接入。楼层结算、期末或抓取时刻不能补作完成时刻。Chronicle 规则沿用 ADR 0019。 |
| 海外深渊 | FurinaChronicle 有海外角色绑定，Snap.Hutao.Remastered 有单独的 `GameRecordClientOversea`。 | 本纵切拒绝海外 Realm。海外凭据、权限、期次和响应需单独实测。 |
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
