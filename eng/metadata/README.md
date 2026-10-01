# 卡池事件内置数据更新

`FurinaChronicle.Infrastructure/Gacha/Metadata/Assets/genshin-gacha-events.v1.json` 是应用的离线卡池事件基线，不应手工编辑。当前基线来自 Snap.Metadata 简体中文 `Genshin/CHS/GachaEvent.json`，只声明适用于国服官方服和 B 服。

更新前先记录来源提交和提交时间，然后运行：

```powershell
./eng/metadata/Export-GachaEventDataset.ps1 `
    -SourcePath <Snap.Metadata>/Genshin/CHS/GachaEvent.json `
    -OutputPath ./FurinaChronicle.Infrastructure/Gacha/Metadata/Assets/genshin-gacha-events.v1.json `
    -SourceRevision <完整提交哈希> `
    -SourceUpdatedAt <ISO-8601 提交时间>
```

生成器会把同期 `301` 和 `400` 横幅合并到同一角色活动祈愿区间，同时保留两个横幅、原始抽卡类型、UP 物品 ID 和两个候选图片 URL。稳定 ID 不使用卡池名称和图片 URL。

更新后必须：

1. 核对来源记录数、事件区间数、首个开始时间和最后结束时间；
2. 查看生成文件差异，确认没有丢失已有历史区间；
3. 更新 `THIRD-PARTY-NOTICES.txt`（仅当来源、版权或许可证变化）；
4. 运行 `dotnet test FurinaChronicle.Tests/FurinaChronicle.Tests.csproj`；
5. 构建 Windows 目标并人工检查历史页卡片和横幅缓存操作。

国际服不得直接复用这份时间范围。新增国际服来源时，应生成独立区服数据集并在目录合并层按 `GameServerRegion` 选择。
