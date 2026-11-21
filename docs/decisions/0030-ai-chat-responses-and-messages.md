# ADR 0030：AI Chat Completions、Responses 与 Messages 协议

状态：Accepted（维护者 2026-11-22 明确要求支持 OpenAI Chat Completions、OpenAI Responses、Anthropic Messages）。扩展 ADR 0029 的首版接口范围；其配置、密钥隔离、会话与逐次授权规则保持有效。

- 新配置的接口选择为三种原生协议。设备已存在的 Ollama 原生配置保留兼容入口，不转换为其他协议；旧枚举数值 0/1 和名字不变，以保留安全存储槽及旧文件语义。新增值仅追加。旧 Envelope v2 可读；新增全局偏好写入 Envelope v3，避免旧开发版重新保存时丢失提示词/工具配置；旧版遇到 v3 必须拒绝而不是重建。
- Chat Completions 使用 messages、Bearer 和 chat/completions SSE；Responses 使用 input、Bearer 和 responses SSE，显式 store=false，每次携带本地已审查上下文，不使用 previous_response_id、服务端会话、后台模式或远程文件。
- Anthropic 使用 messages 与独立 system、x-api-key、anthropic-version: 2023-06-01 和 messages SSE。此仍受官方支持的基础版本服务于既有兼容网关；不发送温度或 beta 参数。模型列表使用同一供应商的认证，不发送聊天内容，最多 500 项；分页仅构造本机已知路径，不跟随服务端 URL。
- 图片仅使用已审查的本地内嵌字节，分别编码 image_url / input_image / image.source。不开启远程图片下载或文件托管。
- 按实际终止事件判断完成；截断、拒绝和缺失终止保持不完整。默认拒绝未授权工具/函数调用；经后续 [ADR 0031](0031-ai-chat-preferences-and-read-only-tools.md) 明确启用时只执行白名单本地只读查询。思考内容与签名不作为回答显示或持久化，仅按协议在当前用户轮临时续接。
- 不自动在协议间回退、重试或再次发送资料。更换协议仍隔离密钥，并要求重新审查。错误和诊断不回显服务端正文或秘密。
- 不新增 AI SDK、业务 Schema、云会话或公开交换格式。用户自行配置并承担服务费用；界面、日志及本地聊天现有隐私规则继续有效。

协议依据（2026-11-22 核对）：[OpenAI Responses](https://developers.openai.com/api/reference/resources/responses/methods/create)、[Responses streaming](https://developers.openai.com/api/docs/guides/streaming-responses)、[Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create)、[Anthropic streaming](https://platform.claude.com/docs/en/build-with-claude/streaming)、[API versions](https://platform.claude.com/docs/en/api/versioning)。

协议内必要的思考签名/加密片段仅在同一个用户轮内存中续接给原服务，不作为回答显示，不保存到会话、报告或日志，也不跨用户轮回放。Responses 请求工具时显式请求 reasoning.encrypted_content，仍保持 store=false。
