# 三种 AI 协议、全局配置与只读工具验收

2026-11-22；基线 `master / 98ccedd`，起始工作区干净。业务 Schema 12、Version.props 不变。契约为 [ADR 0030](../decisions/0030-ai-chat-responses-and-messages.md) 与 [ADR 0031](../decisions/0031-ai-chat-preferences-and-read-only-tools.md)。

## 实际范围

- 新接口选择：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages。旧 Ollama 记录的编码与安全槽保持，只在旧记录显示兼容入口，不自动转换。
- 文本、连续对话、内嵌图片、原生函数调用及对应 SSE 终止；Responses 显式 store=false，无服务端会话/后台模式；Anthropic 使用独立 system 与 x-api-key、固定兼容版本 2023-06-01，不携带 Bearer 或不支持的采样参数。
- AI 设置入口中的全局称呼（仅本地）、系统提示词、工具开关及 0～500 次上限，默认 You / 中文分析提示 / 关闭 / 10 次。全局配置单独保存；供应商操作不覆盖它。
- 工具仅筛选当前资料快照、读取 E 编号并核验来源可用性、查询既有抽卡统计；无 SQL、UID、路径或 URL 入参。并不开放完整数据库、文件、命令或联网工具。
- 每轮明确审查真实请求地址、接口、提示词、资料及工具范围；达到上限后只允许最终回答。未知调用、非法参数、存储失败及超额停止，失败查询也记录未确认完成的摘要。
- 思考签名/加密片段仅本轮内存续接，不显示或写入聊天/报告/日志。工具原始参数与结果不落盘，只保存安全摘要；旧会话仍可读，关联删除保护不变。
- 设备配置写入 Envelope v3，可读取 v2；旧客户端遇到 v3 应拒绝，保护新全局偏好。聊天仍为内部 v1，新增可选工具摘要；内部旧开发版不承诺新字段往返保真。

## 验证结果

| 项目 | 状态 | 证据及限制 |
|---|---|---|
| 全套 Release 测试 | Pass | `ai-protocols-final.trx`：1,457 通过，0 失败/跳过；其中 127 项 AI 测试 |
| 原生协议自动化 | Pass | 文本/图片/独立认证、UTF-8 碎片、Responses/Anthropic 截断与缺失终止、错误正文不回显、分页仍绑定原供应商 |
| 工具与偏好自动化 | Pass | 三协议完整工具往返、关闭/0 次/上限/无资料/无宿主、UID 授权、跨范围/SQL/文件参数拒绝、只读 SQLite 无事实/变更集写入、临时片段与工具原结果不落盘、全局偏好保留与旧 v2 读取 |
| Windows Debug | Pass | VS 18 MSBuild / Debug / x64 最终构建成功；可见 UI 另列 |
| Windows 可见 UI | Pass（合成根） | 三协议下拉值与持久化数值对应；保存全局称呼/提示/开关/次数；历史消息显示自定义称呼；发送审查包含提示词、实际 Messages 地址及工具范围/次数，不含本地称呼或 UID；取消不新增发送消息 |
| Chat Completions 真服 | Pass（合成数据） | /v1/chat/completions 两次 HTTP 200，一次只读统计工具后完成回答 |
| Responses 真服 | Pass（合成数据） | /v1/responses 两次 HTTP 200，一次只读统计工具后完成回答 |
| Anthropic Messages 真服 | Not verified | 当前提供的测试入口 /v1/messages 返回 HTTP 404。不能据此断言客户端失败，也不能标记真服已通过；没有自动切换协议或重发资料 |
| Android Debug 含程序集 | Pass | 显式使用 E:\AndroidSDK 与 EmbedAssembliesIntoApk=true；0 警告/错误；签名 APK 93,051,984 bytes，已确认 lib/arm64-v8a/lib_FurinaChronicle.App.dll.so；未安装 |
| Android 真机 | Not run | 待人工验证协议选择、全局设置、系统返回和发送审查 |

工程证据位于 `FurinaWorkspace/.validation/ai-protocols-20261122/`：`live-native-summary.json`、`windows-native-preferences-summary.json`、全局配置/协议选择/工具审查截图。Windows 使用既有受控 `challenge-layout-20261120/window` 合成库；真实模型请求只发送合成证据/统计，不发送玩家记录或 UID。本轮没有安装或发布应用。

## 人工补测

1. 设置 → AI 设置：查看三种新协议，填写对应 API 基址（例如包含 /v1 的基址），实际请求分别追加 chat/completions / responses / messages。更换协议不会携带旧安全槽密钥，需为新协议明确设置密钥。
2. 保存全局称呼和提示词，返回聊天；称呼仅本地显示，提示词出现在发送审查中。普通聊天不附加资料时不声明档案工具。
3. 附加合成档案，开启工具并设为 1；审查工具范围/次数、实际服务与 UID 规则，确认后观察最多一次查询和最终回答；再关闭工具确认不执行调用。
4. Android 真机重复以上步骤及二级页面返回/系统返回。取消发送不新增已发送轮次；停止保持未完成状态，之后打开会话能够查看工具安全摘要。
5. 使用确实开放 Anthropic Messages 的账号/入口完成文字、图片和只读工具往返；当前测试入口的 404 保留在记录中，不通过修改认证方式或偷偷回退为其他协议伪造成功。

参考：[OpenAI Responses](https://developers.openai.com/api/reference/resources/responses/methods/create)、[OpenAI 函数调用](https://developers.openai.com/api/docs/guides/function-calling)、[Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create)、[Anthropic 流式消息](https://platform.claude.com/docs/en/build-with-claude/streaming)、[API 版本](https://platform.claude.com/docs/en/api/versioning)。
