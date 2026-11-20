# AI 分析首版验收与交接

2026-11-21。基线：`master` / `697b07a`，Schema 12，版本配置 `0.10.0 / Phase 10.0`；AI 规则按维护者本轮授权和 [ADR 0027](../decisions/0027-read-only-ai-analysis.md)。实际功能及字段边界见 [AI 架构](../architecture/AI_ANALYSIS.md)。

## 自动化与运行证据

| 验证项 | 状态 | 实际证据 |
|---|---|---|
| 全套 Release 单元/集成回归 | Pass | 1,369 项通过，0 失败/跳过，约 63 秒；`ai-full-suite.trx` |
| AI 定向测试 | Pass | 39 项，涵盖身份、来源、数据隔离、统计、证据、授权、失效草稿、晚到回复、密钥配置和两种流式协议；包含在全套结果中 |
| Windows WinUI x64 Debug | Pass | VS MSBuild 编译；完整多行预览修复后再次编译通过 |
| 用户提供的真实服务 | Pass（仅合成资料） | 模型列表含 `deepseek-flash`；匿名流式回答、追问、有效 E001 引用、明确授权的合成档案 UID 模式均完成 |
| Windows 可见交互 | Pass（受控合成库） | AI 导航、准备资料、完整请求预览、取消、真实流式结果、本地证据弹窗、切换范围清空会话、档案 UID 明确提示、改用编号后重新预览并发送 |
| Windows 无事实写入 | Pass | 前后 24 张非 Chronicle 表逐表内容哈希一致；排除的只有可重建 Chronicle 表；未访问生产玩家数据库 |
| Windows 匿名载荷检查 | Pass | 单账号及档案匿名预览均不含受控库任何账号 UID；`includes_uids=false` |
| Android Debug 含程序集构建 | Pass | 指定 `E:\AndroidSDK`，`EmbedAssembliesIntoApk=true`；6 分 28 秒，0 警告/错误；签名 APK 93,173,738 字节。构建不代表真机验收 |
| Android 真机导航/授权/安全存储/回答 | Not run | 本轮 `adb devices -l` 无连接设备 |
| Android 真机 HTTP Ollama | Not run | 已改用维护者提供的外部服务，不启动本地模型；Android 明文策略另需验证 |
| 原生系统保存对话框导出 Markdown | Not run | 服务层实际生成合成分析报告；本轮未执行系统文件保存交互，不能等同双端保存通过 |

本机受控证据目录：`FurinaWorkspace/.validation/ai-20261121/`（不提交仓库）。其中 `live-summary.json`、`live-*-synthetic.md`、`windows-uia-summary.json`、`windows-wire-audit.json`、`windows-facts-verification.json` 和 `windows-*.png` 均只含合成测试信息，不含服务密钥。Windows 测试使用既有 `challenge-layout-20261120/window` 合成库，测试专用服务密钥只在该测试根的 DPAPI 文件中加密保存；没有替换用户生产账号凭据。

### 真实测试发现并修复的问题

1. `deepseek-flash` 在较低 token 额度下可能只返回思考内容，没有最终正文。补充显式“关闭兼容服务的思考模式”选项；只有勾选才发送 `thinking.type=disabled`。非支持服务默认不发送此参数。真实服务重验成功。
2. WinUI 预览文本框先设置 `Text` 再开启 `AcceptsReturn` 时，只保留首行。调整初始化顺序并提供可滚动的 340 高度；实际截图与读取均验证完整正文。
3. 合成整档案比较达到测试的 1,024 token 上限，应用正确标为未完成。把受控测试输出配置提高至 4,096 后完成。首次小样真实服务测试仍为 1,024，不能把容量不足的一次请求记为成功。

服务提示词可约束输出，但不是可证明的事实校验器。模型仍可能出现解释错误或夸大样例，用户必须结合本地证据复核；本轮未声称任何模型分析结论均正确。

## 重跑工程检查

在仓库根运行，完整构建、测试和设备操作串行；Android SDK 路径只作为本机命令参数，不写入项目。

```powershell
dotnet test src/FurinaChronicle.Tests/FurinaChronicle.Tests.csproj --configuration Release --filter "FullyQualifiedName~FurinaChronicle.Tests.Ai"
dotnet test src/FurinaChronicle.Tests/FurinaChronicle.Tests.csproj --configuration Release --no-restore
& 'G:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' src/FurinaChronicle.Windows/FurinaChronicle.Windows.csproj /restore /p:Configuration=Debug /p:Platform=x64 /verbosity:minimal /nologo
dotnet build src/FurinaChronicle.App/FurinaChronicle.App.csproj -f net10.0-android -c Debug -p:AndroidSdkDirectory=E:\AndroidSDK -p:EmbedAssembliesIntoApk=true
pwsh -File eng/copyright/Manage-CopyrightHeaders.ps1 -Mode Check
git diff --check
```

真实付费服务测试不纳入 CI，也不把维护者的密钥文件加入测试资源。协议自动测试使用专用虚构 handler、合成库和假密钥。真实服务配置由操作人单独提供，不把密钥放在命令参数、截图、通知、诊断或报告中。

## Windows / Android 人工或 UIA 补测

每项分别记录 `Pass / Fail / Blocked / Not run`、构建版本/SHA、平台、证据文件；只能对自己的受控数据和获准服务操作。

1. 从“AI 分析”进入。“模型服务”填写兼容 API 基址、模型与密钥，按服务能力选择思考开关。先“查看模型（不发送档案）”，再保存配置。确认密钥框清空，重新进入仍能使用该服务，修改接收方不会沿用旧服务密钥。
2. 选择单账号和数据类别，“准备本次资料”。“查看资料与证据”中没有 UID、档案名、昵称、备注、实例 ID、外部记录 ID、凭据或图片地址。未知发生时间为空，Partial 不被改成 Complete。
3. 输入分析问题，点击“审查并发送”，逐段滚动检查完整正文及服务/模型。取消应返回且不生成回答；再次发送确认后流式生成。对照 E 编号检查对应摘录和本地源记录。有效编号也需检查模型是否正确解释。
4. 切换整个档案，默认勾选 UID。弹窗必须明确展示 UID 发送说明及“同意携带 UID 并发送 / 改用编号 / 取消”。选择取消无发送；选择“改用编号”应重建匿名资料并再次确认，不直接发送。
5. 用至少两个 UID 顺序打乱的合成账号验证编号按数值升序分配；匿名预览不出现 UID，资料统计仍归属于各自编号。相同角色跨档案事实独立，不合并保底。
6. 整档案 UID 模式明确同意后完成分析；追问仍须再次出现 UID 确认。关闭弹窗不应沿用上次授权。
7. 生成时取消，或更改账号/档案/日期/类别、离页再进入。旧会话应清空，晚到响应不得出现于新账号。截断/失败回答显示未完成，后续请求不将其作为完整对话回放。
8. 在临时目录导出 Markdown，确认这是模型分析报告、含范围/来源/证据/限制，能离线打开；不混入领域 Portable/Archive。取消保存不报成功。Android 使用系统文件保存界面并重启验证安全存储。
9. 可设虚构无效密钥、不可达服务、输出上限制造错误/取消；通知只显示安全错误，不应包含请求、提供商错误正文或秘密。不通过实际不可逆修改业务数据来验证 AI。

Windows UIA 优先 AutomationId：`NavigationAi`、`AiArchive`、`AiAccount`、`AiPrepare`、`AiQuestion`、`AiSend`、`AiSendReview`、`AiReply`、`AiEvidence`。页面全局滚动后重新获取元素；只点击可见项，弹窗关闭动画结束后再下一步。多行文本优先 TextPattern，不以首屏文字推断完整载荷。Android 底部“AI 分析”进入，发送审查是独立模态页，系统返回等同取消；不能以模拟器代替真机结论。

## 后续边界

尚未实现：AI 会话持久化/备份、工具调用或 AI 自动写入、模型自动采集、专属云 AI 计费、完整装备/天赋分析、所有明细无界上传、可证明的模型结论正确性。没有这些能力不影响首版只读分析；Android 真机和原生保存的未运行项继续保留，不能通过构建自动关闭。
