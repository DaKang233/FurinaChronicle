# AI 设置卡片入口验收

2026-11-22；起始基线 `master / 0114719`，工作区干净。仅调整展示入口，模型供应商、密钥、聊天、发送确认和业务数据库 Schema 不变。

## 实施

- 设置页以与其他设置项一致的 Border 卡片包裹 AI 说明及“AI 设置”按钮（`AiSettingsEntry`），不再展开配置表单。
- WinUI 点击后打开可滚动 ContentDialog（`AiSettingsDialog`），底部“关闭”固定保留。模型列表或删除确认暂时替换父弹窗，操作完返回同一编辑器，保持草稿和滚动位置；不同时打开两个 ContentDialog。存储/列表请求进行中不允许关闭父编辑器。
- MAUI 通过普通 Navigation.PushAsync 进入 `AiSettingsPage`，提供 `AiSettingsBack`“返回设置”按钮；原生返回沿用导航栈。编辑器放在滚动区域，只在首次显示时加载；原生选择器/前后台恢复不重置草稿。
- 只有明确保存/删除修改配置；关闭或返回不自动保存输入，再次进入读取保存状态。普通设置页不会提前加载 AI 编辑器或获取模型列表。

## 验证

| 项目 | 结果 | 实际证据 |
|---|---|---|
| WinUI x64 Debug | Pass | VS MSBuild 编译成功 |
| Windows 可见操作 | Pass（合成根） | 卡片入口、打开/滚动/关闭/重开；子删除确认取消保留草稿；确认删除模型和供应商后返回父弹窗；显式保存生效，关闭不保存草稿 |
| AI 回归测试 | Pass | 现有 86 项 AI 测试全部通过，0 失败/跳过；`ai-settings-entry.trx`；纯布局不新增实现镜像测试 |
| Android Debug 含程序集 | Pass | 使用 E:\AndroidSDK 与 EmbedAssembliesIntoApk=true；0 警告/错误，已检查 ARM64 主程序集内嵌 |
| Android 真机/系统返回 | Not run | 仍需人工核验二级页面和系统返回，不声称已通过 |

证据位于 `FurinaWorkspace/.validation/ai-settings-entry-20261122/`：`windows-settings-entry-summary.json`、`windows-settings-ai-entry-card.png`、`windows-ai-settings-dialog.png`、`windows-ai-child-confirm.png`。使用既有 `challenge-layout-20261120/window` 合成数据根，没有读取生产玩家数据或发出聊天请求。

Windows 脚本首次因编辑器恢复后控件位于滚动区域上方而未找到可见输入框；调整为先找控件再滚动定位后完整重跑通过，不将此脚本定位错误算作产品失败。

## 补测步骤

1. 在设置页确认 AI 与外观、存储等卡片对齐，只留下说明和入口；未打开时没有模型/密钥输入框。
2. Windows 点击“AI 设置”，向下滚动到额度和保存按钮；取消删除确认后填写内容仍在，确认后返回同一编辑器。关闭再开，应读取已保存值。
3. Android 点击入口进入二级页面；测试“返回设置”、系统返回及重新进入，确认不自动保存未提交内容。确认保存后再返回，聊天中仍能使用保存的供应商/模型。
4. 在模型选择、系统选择器或前后台恢复后，确认正在填写的设置没有被重新加载覆盖。密钥仍不回填明文，模型列表仍只在主动点击时请求。

构建命令及配置规则沿用 [供应商验收](AI_PROVIDERS_ACCEPTANCE.md) 和 [本地聊天验收](AI_LOCAL_CHAT_ACCEPTANCE.md)。
