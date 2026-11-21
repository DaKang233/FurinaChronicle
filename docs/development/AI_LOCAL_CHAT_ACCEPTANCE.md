# 本地 AI 聊天实施与验收

更新：2026-11-22；本轮从 `master / 8d4190f` 的干净工作区开始。Schema 12、版本配置 `0.10.0 / Phase 10.0` 不变。契约见 [ADR 0028](../decisions/0028-local-ai-chat-and-attachments.md)，当前使用说明见 [AI 架构](../architecture/AI_ANALYSIS.md)。

## 已实现

- 双端独立聊天工作区，支持不附加游戏数据的普通聊天。
- 本地会话列表、按标题/本地 UID 搜索、新建、重开、重命名、置顶、确认删除。
- 多轮历史持久化；不再丢弃第 12 轮之前记录。网络请求按额度选取最新完整问答，并告知省略数量；本地消息不因此删除。
- 停止、取消、失败/截断状态、生成前持久化、生成中检查点、重开后识别中断。
- 编辑/重新生成建立副本，保留原对话；复制消息/代码和 Markdown 报告。
- 文本/JSON/CSV/Markdown/XML/YAML 及静态 PNG/JPEG/WebP 附件；本地副本随会话保留。
- 图片元数据清理、人工像素隐私确认和显式模型图片能力开关。
- 附加既有游戏资料/证据；单账号去 UID，整档案逐次确认及匿名回退。更换资料另开会话。
- 全部模型配置集中到设置页；聊天页只留设置入口和模型名称。
- 关联 AI 快照存在时阻止游戏记录/账号/档案永久删除，要求先手动处理会话；副本准备与删除共享协调锁。

## 验证结果

| 项目 | 状态 | 证据及边界 |
|---|---|---|
| 全套 Release 自动化 | Pass | 1,397 通过，0 失败/跳过，约 30 秒；含 67 项 AI 测试；`ai-local-chat-final.trx` |
| 会话持久化/草稿/编辑副本 | Pass（自动化） | 重开和续聊、草稿清理、重命名/置顶/删除、15 轮保留及网络省略 |
| 保存失败/竞态 | Pass（自动化） | 模拟存储失败时 0 次付费请求；初次保存期间切换后旧请求不发送、不清除新草稿；未提交临时片段清理 |
| 永久删除保护 | Pass（真实合成 SQLite） | 记录、账号、档案删除被阻止时事实/版本未改变；手动删会话后可删除；副本创建/删除串行；损坏文件阻止无法核实的删除；已提交重试保留 AlreadyCommitted |
| 附件隐私和协议 | Pass（自动化） | JSON 凭据拒绝、UID 清理、PNG 文本元数据清理、尺寸/长度限制、图片人工确认、两种图片协议、密钥防回显、JSON/CamelCase/Netscape 凭据文件拦截 |
| 真实服务 | Pass（仅合成数据） | `deepseek-flash`：JSON + PNG 一起发送成功；重开本地记录恢复 2 个附件，继续追问成功；出站审计无测试 UID、文件名和实际服务密钥 |
| Windows x64 Debug | Pass | VS MSBuild 构建通过；实际使用独立 WinUI 宿主 |
| Windows 可见交互 | Pass（受控库） | 取消未新增已发送消息；带历史图片追问保存第三轮；重命名/置顶；AI 设置迁移；正常关闭再启动后历史仍在；恢复附件草稿、长文件名移除及图片默认未确认/确认后预览 |
| 游戏数据库未被聊天改变 | Pass | 前后 24 张非 Chronicle 表内容哈希一致；排除的仅是可重建时间线表 |
| Android Debug 含程序集 APK | Pass | 最终源代码构建通过，0 警告/错误；显式使用 E:\AndroidSDK 与 EmbedAssembliesIntoApk=true，APK 93,272,042 字节；尚未真机安装 |
| Windows 系统附件选择器完整点选 | Blocked（自动化） | 已尝试打开；选择器由独立进程承载，未取得可靠所属窗口定位，因此未向未知窗口输入。关闭了受控测试应用，未关闭其他选择器进程。不能把 Reader/真实服务测试等同完整点选通过 |
| Android 真机 | Not run | 本轮 ADB 无连接设备；构建和自动化不能替代真机 |
| 系统保存窗口/完整强杀生成测试 | Not run | 已真实生成 Markdown 文件，并自动化验证中断状态恢复；未声称完成系统保存对话框或流式生成中物理强杀验收 |

受控证据在 `FurinaWorkspace/.validation/ai-chat-20261121/`，目录按任务开始日命名，不提交仓库。包括 `live-chat-summary.json`、`live-chat-report.md`、`windows-chat-summary.json`、`native-attachment-summary.json`、`facts-verification.json`、`pending-attachment-summary.json` 和 Windows 截图。

Windows 测试使用既有 `challenge-layout-20261120/window` 合成库，没有读取生产玩家记录。真实服务密钥仅在内存及受控测试根的 DPAPI 文件中使用，不写入聊天、报告、日志、截图或提交。真实协议测试通过不代表模型关于图片/统计的每项解释均正确。

## 真实交互发现并修复

未进入 AI 页时，主导航也会调用页面 Leave。最初该回调保存了空会话，并抢占最近历史，使启动显示空聊天。现以页面已进入且会话初始化完成为保存条件；重新执行了 Windows 重启测试，已确认恢复原会话。旧首版的 WinUI 多行预览初始化顺序修复继续保留。

## 复核命令

```powershell
dotnet test src/FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release --filter "FullyQualifiedName~FurinaChronicle.Tests.Ai"
dotnet test src/FurinaChronicle.Tests/FurinaChronicle.Tests.csproj -c Release --no-restore
& 'G:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' src/FurinaChronicle.Windows/FurinaChronicle.Windows.csproj /p:Configuration=Debug /p:Platform=x64 /verbosity:minimal /nologo
dotnet build src/FurinaChronicle.App/FurinaChronicle.App.csproj -f net10.0-android -c Debug -p:AndroidSdkDirectory=E:\AndroidSDK -p:EmbedAssembliesIntoApk=true
pwsh -File eng/copyright/Manage-CopyrightHeaders.ps1 -Mode Check
git diff --check
```

真实付费模型不加入 CI；API 密钥只由维护者在本地设置，不进入命令参数。使用合成数据，完整构建、测试与设备交互串行。

## Windows / Android 补测指南

1. 从设置页配置模型、密钥、额度及图片能力；获取模型列表，确认未发送聊天资料。返回聊天页，确认不再占用聊天区域显示服务表单。
2. 普通聊天发送两轮，正常退出重开，确认标题、消息与状态恢复。搜索、置顶、重命名；编辑某轮后保留原会话，新副本将问题和附件放回输入框，审查确认后重发。
3. **Windows 系统点选待补测**：点击“文件/图片”，选择一个合成 JSON 和 PNG，确认在输入区出现；打开预览。图片确认默认未选，不确认时不能发送；检查图片像素无 UID 后勾选，审查正文与图片，再确认发送或取消。不要选择真实凭据文件。
4. Android 真机重复第 1～3 项，检查系统返回等于取消，模态返回不重置会话；重启后仍能打开本地附件副本，原文件移走不影响已存记录。
5. 附加单账号资料，预览/网络正文无 UID。附加整个档案默认允许 UID，但每次发送/追问仍明确确认；改用编号后再次预览，同一快照的 E 编号不变。重开 UID 会话不得免除确认。
6. 生成时停止并重开，应保留部分回答且标未完成，后续请求不将其当完整问答。若做真实强杀，只用合成会话和受控数据根；记录 kill 时刻及重新启动后的文件/状态，不以手工构造文件代替真实强杀证据。
7. 在有游戏资料会话时尝试永久删除对应合成记录/账号/档案，应提示先手动处理会话，事实不变。进入 AI 聊天按本地 UID 找到并删除相关会话，再重试应成功。普通聊天和其他档案副本不应误作相同账号数据。
8. 导出 Markdown 到用户选择的临时目录，离线查看文字、模型状态和证据。报告不内嵌图片，也不是可重新导入的备份。取消系统保存不得提示成功。

UIA 稳定入口：`NavigationAi`、`AiNewChat`、`AiConversations`、`AiSearch`、`AiQuestion`、`AiSend`、`AiCancel`、`AiMessages`、`AiAttachFile`、`AiPrepare`、`AiSendReview`、`AiOpenSettings`；设置内沿用 `AiBaseAddress`、`AiModel`、`AiApiKey`、`AiSaveSettings`。原 `AiReply`/卡片式资料流程已替换，不应再照旧首版脚本查找。系统文件对话框可能属于不同进程，必须核实目标/所有权；不向无法确认的桌面窗口输入。

## 后续范围

不实现云会话同步、公开聊天包导入、语音/联网/工具执行、图像生成、PDF/Word 或模型多配置档案。不对用户任意文件做可靠账号归属推断；外部副本和文件中的关联内容由用户管理。未运行的真机/系统交互项保持开放，不能因构建成功而关闭。
