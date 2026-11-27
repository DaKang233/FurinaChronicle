# 通行证会话请求形状（大陆）

状态：2026-11-27 实施。适用于 `FurinaChronicle.Infrastructure/Passport` 与
`HistoryDomains/Collecting` 的大陆方向请求。海外方向（HoYoLAB）形状未变。

## 背景

2026-11 起，米游社对大陆会话按 **Passport v2** 形态识别：

- 派生凭据以 `ltoken_v2` / `cookie_token_v2` 承载，并通过
  `ltmid_v2` / `account_mid_v2` 与账号 MID 绑定；
- 只带 v1 名（`ltoken`、`cookie_token`）或缺少 MID 绑定的请求，
  会被统一拒绝为 `retcode -100`（“登录状态失效，请重新登录”）；
- `-10001 invalid request` 表示请求形状本身不成立（缺 DS、签名代际错误），
  与 `-100` 的语义不同，排查时不要混用。

对照实现：Snap.Hutao.Remastered `e0668e4ec`（Passport v2 credential
exchange）、`3f6575dc7`（Passport 与 GeeTest 修复）。

## Cookie 组成

唯一边界是 `Services/Passport/PassportCookieComposer`，各调用方不得自行拼接。

| 用途 | 方法 | 内容 |
| --- | --- | --- |
| 账号资料、兜底角色查询 | `ForSession` | 全量：`stuid`、`account_id(_v2)`、`ltuid(_v2)`、`mid`、`ltmid_v2`、`account_mid_v2`、`stoken`、`ltoken(_v2)`、`cookie_token(_v2)` |
| SToken 角色查询 | `ForSTokenSession` | `stuid`、`mid`、`stoken` |
| 战绩采集 | `ForGameRecord` | LToken/CookieToken 两套名字 + MID 绑定，**不含** `stoken` |
| 计算器等 Web 端点 | `ForWebToken` | `account_id(_v2)`、`account_mid_v2`、`cookie_token(_v2)` |

海外（HoYoLAB）方向发送 v1 与 v2 两套名字的并集：其 Web 会话使用
`_v2`，而账号端点仍读取 `ltuid`/`ltoken` 等 v1 名。

规则：

- 同一凭据必须同时以 v1 与 v2 名出现；任一缺失都可能导致会话不被识别。
- 服务端轮换某个凭据时，同组别名必须一起替换，
  见 `HoYoGameRecordSpiralAbyssSource.CredentialCookieAliases`。
- 值必须通过 `IsHeaderToken` 校验，含分隔符的值不进入请求头。

## 端点与签名代际

| 用途 | 端点 | 签名 |
| --- | --- | --- |
| 角色列表 | `api-takumi.miyoushe.com/binding/api/getUserGameRolesByStoken` | Gen1 + K2 |
| 账号资料 | `bbs-api.miyoushe.com/user/api/getUserFullInfo?uid=…` | Gen1 + K2 |
| 角色兜底 | `api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie` | Gen1 + X4 |
| 派生凭据交换 | `passport-api.mihoyo.com/account/ma-cn-session/app/exchange` | Gen2 + Passport |
| 战绩 | 各 `game_record` 端点 | Gen2 + X4 |

- Gen1：`{t},{r},{md5(salt&t&r)}`，角色端点要求 6 位字母数字 nonce。
- Gen2：追加 `&b={body}&q={query}`，签名的 body 必须与实际发送的字节一致。
- v1 端点（`getLTokenBySToken`、`getCookieAccountInfoBySToken`）保留为回退，
  仅在交换端点拒绝时使用；两条路径都失败才判定为凭据不可用。

## 不变量

- 角色列表与账号资料只依赖根 SToken；派生凭据交换失败不得导致登录失败，
  也不得隐藏角色列表。资料读取失败只降级显示名。
- 交换失败以 `PassportTokenExchangeException` 记录 `retcode`，
  不记录任何凭据内容。
- 设备上下文（`x-rpc-device_id` 与 `x-rpc-device_fp`）在登录、资料、
  战绩之间保持一致；指纹存在时每个请求都要携带。

## 安全验证（GeeTest V3 / V4）

- V3：`X-Rpc-Aigis` 的 `data` 含 `gt` + `challenge`；回传
  `{geetest_challenge, geetest_validate, geetest_seccode}`。
- V4：`data` 含 `captcha_id`（可能只出现在 `gt`）与可选 `risk_type`；
  回传验证对象本身（`lot_number`、`captcha_output`、`pass_token`、
  `gen_time`），缺任一字段即拒绝。
- 两种形态都以 `{session_id};{base64(proof)}` 作为 `x-rpc-aigis`。
- UI 侧 V4 使用 `https://static.geetest.com/v4/gt4.js` 与
  `initGeetest4({ captchaId, riskType, userInfo })`，结果原样交给客户端；
  WinUI 与 MAUI 两个宿主都必须支持，否则海外登录在验证环节中断。

## 验证

1. `dotnet test src/FurinaChronicle.Tests/FurinaChronicle.Tests.csproj`
   （`PassportCookieComposerTests`、`MiHoYoAccountProfileClientTests`、
   `MiHoYoPassportClientTests`）。
2. 真实账号复测时只看状态类别：`0` 为通过，`-100` 为会话形态不对，
   `-10001` 为请求形状不对。不要把响应正文或凭据写入日志、截图或提交。
