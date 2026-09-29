# 备份与恢复

状态：Target

## Archive 结构

```text
manifest.json
datasets/
settings/portable.json
attachments/
checksums.sha256
```

Manifest 至少记录格式版本、应用版本、导出时间、来源平台、数据集版本、路径、记录数量、哈希和可选加密信息。

全量备份使用逻辑数据，不把 SQLite 文件作为跨平台公开契约。可重建缓存和设备设置默认不进入备份。

## 设置分类

- Portable：语言、统计偏好、数据保留策略、`RequireUniqueArchiveNames`、撤销次数设置和可跨端布局。
- Device-local：Windows 游戏路径、Android SAF URI、窗口位置、权限状态和 DeviceId。
- Secret：Cookie、SToken、密钥和验证码。

普通 Archive 只包含 Portable 设置。Secret 默认排除；未来如提供凭据保险库，必须独立加密、独立选择并明确风险。

如果恢复的唯一名称设置与目标档案现状冲突，必须在恢复预览中解决，不能把应用直接置于无法正常启动的状态。

## 恢复模式

- `Merge`：按档案和账号映射合并，不因缺失删除目标数据。
- `Replace`：明确选择后，用备份重建所选范围。

两种模式都必须先生成恢复预览。恢复过程在临时模型或临时数据库中完成验证，正式提交使用原子事务或等价安全切换。

## 完整性与真实性

- SHA-256 用于发现损坏，不证明由谁创建，也不防止攻击者修改后重新计算哈希。
- AES-GCM 在持有正确密钥时提供密文完整性，不证明创建者身份或备份新旧顺序。
- 数字签名和防回滚属于独立能力，当前离线备份不要求 PKI。

## 验收要求

- 损坏文件和不支持版本不会改变数据库。
- 重复 Merge 幂等。
- Replace 前后记录数量和引用完整。
- 恢复中断可安全重试。
- Windows 导出可由 Android 恢复，反向同样成立。
- 默认备份不包含秘密和设备专用路径。
