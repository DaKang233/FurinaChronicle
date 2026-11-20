// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Ai;

/// <summary>Read-only, explicit whitelist projection. No passport access; only existing read queries are called.</summary>
public sealed class BuildAiContext(IPlayerArchiveRepository archives, IGameAccountRepository accounts,
    IGachaRecordRepository gacha, BuildGachaAnalytics analytics, IArchiveDomainHistoryStore history,
    IDomainMetadataService metadata, IChronicleQueryService chronicle)
{
    public async Task<PreparedAiContext> ExecuteAsync(AiAnalysisScope scope, int characterBudget = 24000,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        if (characterBudget is < 4000 or > 1_000_000) throw new AiAnalysisException("资料长度设置无效。");
        var archive = await archives.GetByIdAsync(scope.ArchiveId, cancellationToken)
            ?? throw new AiAnalysisException("档案已不存在，请重新选择。");
        var all = await accounts.GetByArchiveIdAsync(archive.Id, cancellationToken);
        var selected = all.Where(item => scope.AccountId is null || item.Id == scope.AccountId)
            .OrderBy(item => ulong.TryParse(item.Uid, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) ? n : ulong.MaxValue)
            .ThenBy(item => item.Uid, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray();
        if (selected.Length == 0 || selected.Length > 20) throw new AiAnalysisException("未找到所选账号，或档案账号数超过首版上限 20。");
        string[] privateIds = selected.SelectMany(item => new[] { item.Uid, item.Id.ToString(), item.DisplayName ?? "",
            item.GameRoleIdentityId?.ToString() ?? "" }).Append(archive.Name).Append(archive.Id.ToString()).Where(x => x.Length > 0).Distinct().ToArray();
        bool includeUids = scope.AccountId is null && scope.AllowArchiveUids;
        var aliases = selected.Select((item, index) => (item.Id, Label: $"账号 {index + 1:00}" +
            (includeUids && item.Uid.Length is >= 8 and <= 12 && item.Uid.All(char.IsAsciiDigit) ? $"（UID {item.Uid}）" : "")))
            .ToDictionary(item => item.Id, item => item.Label);
        var stats = new List<AiPoolSummary>();
        var evidence = new List<AiLocalEvidence>();
        var notes = new List<string>
        {
            "资料仅来自本地已保存记录，不代表官方完整历史。统计为确定性计算，模型结论是推断，不是新的游戏事实。",
            "日期使用 UTC 日粒度；发生、观察和抓取时间分开。未知发生时间保持未知。",
            "抽卡均值仅针对已有相邻记录间隔；首段未知保底不冒充完整间隔。观察完整度只相对于该类数据的声明范围。",
            "资料为准备时各领域的独立历史读取，不声明它们发生在同一时点。证据示例有数量及字符上限。"
        };
        int categoryCount = Enum.GetValues<AiDatasets>().Count(value => value is not (AiDatasets.None or AiDatasets.All) && scope.Datasets.HasFlag(value));
        int quota = Math.Max(1, scope.MaxEvidence / (selected.Length * Math.Max(1, categoryCount)));
        var nameCache = new Dictionary<(InventoryItemKind, string), string>();
        async Task<string> Name(InventoryItemKind kind, string? id)
        {
            string fallback = kind == InventoryItemKind.Character ? "未知角色" : "未知武器";
            if (id is null || id.Length is < 4 or > 8 || !id.All(char.IsAsciiDigit)) return fallback;
            if (nameCache.TryGetValue((kind, id), out string? existing)) return existing;
            var item = await metadata.ResolveItemAsync(kind, id, cancellationToken);
            string safe = item?.Source is "Snap.Metadata" or "GenshinCalculator"
                ? AiPrivacy.PublicName(item.Name, privateIds, fallback) : fallback;
            nameCache[(kind, id)] = safe;
            return safe;
        }
        void Add(string alias, string dataset, string description, Guid accountId, string entityKind, string entityId,
            DateTimeOffset? occurred, RecordProvenance provenance, string coverage, string? entryKey = null)
        {
            if (evidence.Count >= scope.MaxEvidence) return;
            var fact = new AiEvidenceFact($"E{evidence.Count + 1:000}", alias, dataset, Day(occurred),
                Day(provenance.Timestamps.ObservedAt), Day(provenance.Timestamps.FetchedAt), provenance.Origin.ToString(), coverage, description);
            evidence.Add(new(fact, accountId, entityKind, entityId, entryKey));
        }
        foreach (var account in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string alias = aliases[account.Id];
            if (scope.Datasets.HasFlag(AiDatasets.Gacha))
            {
                var query = new GachaRecordQuery([account.Id], StartTime: scope.FromUtc, EndTime: scope.ToUtcExclusive?.AddTicks(-1), Limit: null);
                int count = await gacha.CountAsync(query, cancellationToken);
                if (count > 200_000) throw new AiAnalysisException("抽卡范围超过 200,000 条，请缩小日期范围后准备资料。");
                var report = await analytics.ExecuteAsync(query, GachaAnalyticsComponents.Pools, cancellationToken);
                foreach (var pool in report.Pools)
                    stats.Add(new(alias, pool.PoolGroup.ToString(), pool.TotalPulls, pool.FiveStarCount, pool.FourStarCount,
                        pool.ThreeStarCount, pool.AverageFiveStarPulls, pool.AverageUpFiveStarPulls,
                        pool.PullsSinceLastFiveStar, pool.PullsSinceLastFourStar));
                var records = await gacha.QueryAsync(query with { RankTypes = new HashSet<int> { 4, 5 }, Limit = quota }, cancellationToken);
                foreach (var record in records)
                {
                    var kind = record.ItemId?.StartsWith("100", StringComparison.Ordinal) == true ? InventoryItemKind.Character : InventoryItemKind.Weapon;
                    Add(alias, "gacha", $"抽得 {record.RankType?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 星{await Name(kind, record.ItemId)}，数量 {record.Count}。",
                        account.Id, "gacha", record.ExternalRecordId, record.Time, record.Provenance, "单条已保存抽取；历史完整度未知");
                }
                notes.Add($"{alias}：抽卡统计覆盖范围内 {count} 条记录；高星证据示例 {records.Count} 条，未发送全部明细。");
            }
            if (scope.Datasets.HasFlag(AiDatasets.Items))
            {
                var page = await history.GetItemObservationPageAsync(new(account.Id, ObservedFrom: scope.FromUtc,
                    ObservedTo: scope.ToUtcExclusive, Limit: 100), cancellationToken);
                // Round-robin across snapshots preserves temporal comparisons under a finite evidence budget.
                var snapshots = page.Items.Take(8).ToArray();
                int added = 0;
                for (int i = 0; added < quota && i < snapshots.Select(item => item.Entries.Count).DefaultIfEmpty().Max(); i++)
                foreach (var observation in snapshots)
                {
                    if (added >= quota || i >= observation.Entries.Count) continue;
                    var entry = observation.Entries[i];
                    string level = entry.FieldCoverage.HasFlag(InventoryFieldCoverage.Level) ? entry.Level?.ToString(CultureInfo.InvariantCulture) ?? "未知" : "未覆盖";
                    string progress = entry.FieldCoverage.HasFlag(InventoryFieldCoverage.Progression) ? entry.Progression?.ToString(CultureInfo.InvariantCulture) ?? "未知" : "未覆盖";
                    string quantity = entry.FieldCoverage.HasFlag(InventoryFieldCoverage.Quantity) ? entry.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "未知" : "未覆盖";
                    Add(alias, "inventory", $"{await Name(entry.Kind, entry.TypeId)}：等级 {level}，{(entry.Kind == InventoryItemKind.Character ? "命座" : "精炼")} {progress}，数量 {quantity}。",
                        account.Id, "item_observation", observation.ObservationId.ToString(), null, observation.Provenance,
                        $"观察 {observation.CollectionCompleteness}；字段 {entry.FieldCompleteness}，覆盖 {entry.FieldCoverage}", entry.StableItemKey);
                    added++;
                }
                var manual = await history.GetManualAcquisitionPageAsync(new(account.Id, OccurredFrom: scope.FromUtc,
                    OccurredTo: scope.ToUtcExclusive, Limit: Math.Min(500, Math.Max(1, quota / 3))), cancellationToken);
                foreach (var item in manual.Items.Take(Math.Max(1, quota / 3)))
                    Add(alias, "manual_acquisition", $"用户记录获取{await Name(item.Acquisition.Kind, item.Acquisition.TypeId)}；原因类别 {item.Acquisition.Reason}；发生时间类型 {item.Acquisition.OccurredAt.Kind}，范围 {Day(item.Acquisition.OccurredAt.Start) ?? "未知"} ～ {Day(item.Acquisition.OccurredAt.End) ?? "不适用"}。未发送自由备注。",
                        account.Id, "manual_acquisition", item.Acquisition.AcquisitionId.ToString(), item.Acquisition.OccurredAt.Kind == OccurrenceTimeKind.Instant ? item.Acquisition.OccurredAt.Start : null,
                        item.Acquisition.Provenance, $"时间精度 {item.Acquisition.OccurredAt.Precision}；范围边界见本地证据");
                notes.Add($"{alias}：范围内角色／武器观察 {page.TotalCount} 次，取最近最多 8 次的交错条目；手工获取 {manual.TotalCount} 条，仅发送有界示例。日期按观察时间／实际获取时间分别筛选。");
            }
            if (scope.Datasets.HasFlag(AiDatasets.Challenges))
            {
                var page = await history.GetChallengeObservationPageAsync(new(account.Id, ObservedFrom: scope.FromUtc,
                    ObservedTo: scope.ToUtcExclusive, Limit: Math.Min(500, quota)), cancellationToken);
                foreach (var item in page.Items)
                {
                    string description = item.Mode switch
                    {
                        ChallengeMode.SpiralAbyss => $"深境螺旋观察：星数 {item.SpiralAbyssStatistics?.TotalStars?.ToString(CultureInfo.InvariantCulture) ?? item.SpiralAbyssFloors.Sum(f => f.Stars).ToString(CultureInfo.InvariantCulture)}，已保存 {item.SpiralAbyssFloors.Count} 层。不是仅凭星数推断完成。",
                        ChallengeMode.ImaginariumTheater => $"幻想真境剧诗观察：难度代码 {item.Difficulty?.ToString(CultureInfo.InvariantCulture) ?? "未知"}，奖章 {item.MedalCount?.ToString(CultureInfo.InvariantCulture) ?? "未知"}，已保存 {item.Stages.Count} 个阶段。",
                        _ => $"幽境危战观察：单人难度代码 {item.StygianStatistics?.SinglePlayer?.Difficulty?.ToString(CultureInfo.InvariantCulture) ?? "未知"}，最佳总用时 {item.StygianStatistics?.SinglePlayer?.Seconds?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 秒；联机另存 {item.MultiplayerStages.Count} 关。发生时间未知。"
                    };
                    description += $" 期次范围 {Day(item.PeriodStart) ?? "未知"} ～ {Day(item.PeriodEnd) ?? "未知"}（不是完成时间）。";
                    var teams = item.Mode == ChallengeMode.SpiralAbyss
                        ? item.SpiralAbyssFloors.OrderByDescending(f => f.Floor).SelectMany(f => f.Chambers.SelectMany(c =>
                            c.Battles.Select(b => (Label: $"{f.Floor}层{c.Chamber}间{b.Half}半", Members: b.Participants))))
                        : item.Stages.Select((s, index) => (Label: $"阶段 {index + 1}", Members: s.Participants));
                    int teamIndex = 0;
                    foreach (var team in teams.Take(6))
                    {
                        var members = new List<string>();
                        foreach (var member in team.Members.Take(4))
                            members.Add($"{await Name(InventoryItemKind.Character, member.Id)}（等级 {member.Level?.ToString(CultureInfo.InvariantCulture) ?? "未知"}，命座 {member.Constellation?.ToString(CultureInfo.InvariantCulture) ?? "未知"}，来源 {member.Origin?.ToString() ?? "未知"}）");
                        description += $" {team.Label}队伍：{string.Join("、", members)}。";
                        teamIndex++;
                    }
                    description += $" 队伍仅展示前 {teamIndex} 组，未发送敌方机制、自由文本和联机队友身份。";
                    if (item.SpiralAbyssStatistics is { } abyss)
                        description += $" 官方战斗次数 {abyss.TotalBattleTimes?.ToString(CultureInfo.InvariantCulture) ?? "未知"}；胜利次数 {abyss.TotalWinTimes?.ToString(CultureInfo.InvariantCulture) ?? "未知"}。";
                    if (item.TheaterStatistics is { } theater)
                        description += $" 已到达幕数 {theater.MaxRoundId?.ToString(CultureInfo.InvariantCulture) ?? "未知"}；演出总秒数 {theater.TotalUseTime?.ToString(CultureInfo.InvariantCulture) ?? "未知"}；助演次数 {theater.RentCount?.ToString(CultureInfo.InvariantCulture) ?? "未知"}。";
                    Add(alias, "challenge", description, account.Id, "challenge_observation", item.ObservationId.ToString(), null,
                        item.Provenance, item.Completeness.ToString());
                }
                notes.Add($"{alias}：挑战按观察日期筛选，范围内 {page.TotalCount} 次，发送最近 {page.Items.Count} 次。官方未给出的发生时刻不从期次或用时推断。");
            }
            if (scope.Datasets.HasFlag(AiDatasets.Chronicle))
            {
                var page = await chronicle.QueryAsync(new(new(ChronicleScopeKind.Account, account.Id),
                    new(ChronicleTimeline.Main), new(From: scope.FromUtc, To: scope.ToUtcExclusive, IncludeUndated: scope.From is null && scope.To is null),
                    PageSize: Math.Min(500, quota)), cancellationToken);
                if (page.Error != ChronicleQueryError.None) notes.Add($"{alias}：时间线当前不可读取，未用其他数据补造节点。");
                else foreach (var entry in page.Entries)
                {
                    var kind = entry.Domain == ChronicleDomain.Inventory ? InventoryItemKind.Weapon : InventoryItemKind.Character;
                    string name = entry.ItemId is null ? "" : await Name(kind, entry.ItemId);
                    string description = $"派生历程节点：{entry.Domain}/{entry.Kind}，{name}。可信度 {entry.Confidence}。" +
                        (entry.LocalRecordedPulls is int pulls ? $"本地相邻抽取数 {pulls}（非完整保底承诺）。" : "") +
                        (entry.ExactPity is int pity ? $"已证明的精确保底抽数 {pity}。" : "");
                    var provenance = new RecordProvenance(entry.BaseOrigin,
                        new RecordTimestamps(ObservedAt: entry.Time.ObservedAt, FetchedAt: entry.Time.FetchedAt));
                    Add(alias, "chronicle", description, account.Id, entry.SourceEntityType, entry.SourceEntityId,
                        entry.Time.Occurred?.Kind == OccurrenceTimeKind.Instant ? entry.Time.Occurred.Start : null, provenance, $"派生节点 {entry.Completeness}；发生时间类型 {entry.Time.Occurred?.Kind}、精度 {entry.Time.Occurred?.Precision}、范围 {Day(entry.Time.Occurred?.Start) ?? "未知"} ～ {Day(entry.Time.Occurred?.End) ?? "不适用"}；以基础记录为准");
                }
                notes.Add($"{alias}：主时间线仅选当前规则可见的节点，未发送全部领域事实；{(page.NextCursor is null ? "已到本页末" : "还有未发送的后续节点")}。");
            }
        }
        AiContextPacket Packet() => new("genshin", includeUids ? "archive_uid_with_confirmation" : "session_aliases_only",
            scope.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), scope.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            aliases.Values.ToArray(), stats, notes, evidence.Select(item => item.Fact).ToArray());
        bool shortened = false;
        while (System.Text.Json.JsonSerializer.Serialize(Packet(), AiJson.Options).Length > characterBudget && evidence.Count > 0)
        { evidence.RemoveAt(evidence.Count - 1); shortened = true; }
        if (shortened) notes.Add("为遵守资料字符上限，末尾部分证据未发送；统计覆盖说明与实际证据清单分别解释，不把示例当作全部历史。");
        var packet = Packet();
        if (System.Text.Json.JsonSerializer.Serialize(packet, AiJson.Options).Length > characterBudget)
            throw new AiAnalysisException("所选账号的摘要已超过资料上限，请提高上限或缩小范围。");
        var uidAliases = selected.Select((item, index) => new { item.Uid, Alias = $"账号 {index + 1:00}" })
            .Where(item => item.Uid.Length >= 8 && item.Uid.All(char.IsAsciiDigit)).GroupBy(item => item.Uid)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Alias, StringComparer.Ordinal);
        return new(packet, evidence.ToArray(), privateIds, uidAliases);
    }
    private static string? Day(DateTimeOffset? value) => value?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
