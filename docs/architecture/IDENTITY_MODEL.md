# 身份模型

状态：Target

## 目标关系

```text
GameRoleIdentity
    ├── GameAccount (Archive A 中的独立副本)
    │       └── Domain Records A
    └── GameAccount (Archive B 中的独立副本)
            └── Domain Records B

PlayerArchive
    └── GameAccount

ProviderAccount
    └── ProviderRoleLink
            └── GameRoleIdentity

ProviderAccount
    └── CredentialProfile
```

## GameRoleIdentity

`GameRoleIdentityId` 是全局尽量保持稳定的 GUID，用于表示不同档案、设备和导入批次中的账号副本事实上来自同一个游戏角色。自然身份由以下字段共同描述：

```text
GameBiz + Region/Server + UID
```

显示名称不参与身份。

导入规则：

- 目标不存在相同自然身份时，保留源 `GameRoleIdentityId`。
- 目标已有相同自然身份和相同 GUID 时直接复用。
- 目标已有相同自然身份但 GUID 不同时，使用目标 GUID，并记录源 GUID 到目标 GUID 的导入别名映射，避免后续导入反复产生新身份。
- 同一个 GUID 对应不同自然身份视为身份损坏或恶意数据，禁止静默重映射并要求人工处理。

## GameAccount

`GameAccount.Id` 表示档案内的数据副本。它必须关联一个 `PlayerArchiveId`。同一角色在不同档案中的副本拥有不同 `GameAccount.Id`，所有业务记录仍以该 ID 归属，因此数据完全独立。

同一个档案内不得存在两个相同自然身份的 `GameAccount`。目标唯一约束为：

```text
PlayerArchiveId + GameBiz + Region/Server + UID
```

`PlayerArchiveId` 参与档案内副本约束，但不参与游戏角色的全局身份。

## 导入时的本地 ID

- 源包中的 `GameAccount.Id` 只表达包内引用；导入后必须生成目标数据库的新本地 GUID。
- 同一源包中共享一个 `GameRoleIdentityId` 的多个账号副本，导入后仍共享同一个稳定身份 GUID。
- 目标档案已存在相同自然身份时，不创建第二个副本，而是映射到现有副本并执行类别合并策略。

## 档案名称匹配

档案名称采用精确匹配，不进行 Trim、大小写折叠或 Unicode 规范化。`Archive`、`archive`、` Archive` 和 `Archive ` 是四个不同名称。

模糊匹配只用于导入预览提示。它可以采用去除首尾空白、大小写折叠、Unicode 规范化或相似度算法发现“名称类似”的候选，但不得自动决定目标。

导入时：

1. 恰好一个精确匹配项时映射到该档案；
2. 没有精确匹配项时准备创建新档案，同时列出模糊候选，让用户选择已有档案或确认新建；
3. 多个精确同名档案时要求用户手动映射，或选择创建新档案；
4. 源数据中的档案分别导入到其确认后的目标档案。

## 唯一档案名称选项

`RequireUniqueArchiveNames` 默认启用，并属于可移植设置。

启用时：

- 手工创建和重命名不得产生精确同名档案；
- 导入来源若包含多个精确同名档案，导入计划自动使用 `名称`、`名称 (2)`、`名称 (3)` 等确定性编号生成唯一名称，并在提交前明确提示用户；
- 目标没有精确同名档案时可以直接创建，但仍提示模糊相似名称；
- 从关闭切换为开启时，如果数据库已有精确同名档案，则拒绝开启并列出需要手工重命名的档案；
- 启动时若发现数据库被外部修改而违反唯一要求，程序进入阻塞式修复流程。用户必须选择手工改名或自动编号改名，完成后重启应用，正常功能才可继续使用。

关闭时允许同名档案，但导入遇到多个精确匹配项时始终要求手工映射或创建新档案。

## ProviderAccount 与凭据

平台账号只负责取得数据。业务记录不得以 `ProviderAccountId` 作为所有权外键，但来源信息可以记录某次数据由哪个 Provider 和账号取得。凭据默认留在平台安全存储中，不随普通数据迁移。
