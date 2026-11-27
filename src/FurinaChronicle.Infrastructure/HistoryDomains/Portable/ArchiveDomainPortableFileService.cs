// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Portable;

public sealed partial class ArchiveDomainPortableFileService(
    IPlayerArchiveRepository archives,
    IGameAccountRepository accounts,
    IArchiveDomainHistoryStore history)
    : IArchiveDomainPortableFileService
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;
    public const int MaximumFactCount = 100_000;
    private const string FormatName = "furina-domain-history";

    private static readonly JsonSerializerOptions CompactJson = CreatePortableJsonOptions(
        writeIndented: false);
    private static readonly JsonSerializerOptions DisplayJson = CreatePortableJsonOptions(
        writeIndented: true);

    public async Task ExportAsync(
        Guid archiveId,
        ArchiveDomainDatasetKind datasetKind,
        Stream destination,
        DateTimeOffset exportedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        PlayerArchive archive = await archives.GetByIdAsync(
            archiveId,
            cancellationToken) ?? throw new KeyNotFoundException(
                "The archive to export does not exist.");
        IReadOnlyList<GameAccount> archiveAccounts =
            await accounts.GetByArchiveIdAsync(archiveId, cancellationToken);
        var portableAccounts = new List<ArchiveDomainPortableAccount>();
        foreach (GameAccount account in archiveAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameRoleIdentity identity = account.RoleIdentity ??
                throw new InvalidOperationException(
                    "Unresolved accounts cannot be exported as a Portable history dataset.");
            IReadOnlyList<ItemStateObservation> itemObservations =
                datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                    ? await history.GetItemObservationsAsync(
                        account.Id,
                        cancellationToken: cancellationToken)
                    : [];
            IReadOnlyList<ManualAcquisitionState> current =
                datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                    ? await history.GetManualAcquisitionsAsync(
                        account.Id,
                        cancellationToken: cancellationToken)
                    : [];
            IReadOnlyList<PortableManualAcquisitionRevision> revisions =
                datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                    ? (await history.GetManualAcquisitionRevisionsAsync(
                        account.Id,
                        cancellationToken)).Select(ToPortableRevision).ToArray()
                    : [];
            IReadOnlyList<ChallengeObservation> challenges =
                datasetKind == ArchiveDomainDatasetKind.ChallengeHistory
                    ? await history.GetChallengeObservationsAsync(
                        account.Id,
                        cancellationToken: cancellationToken)
                    : [];
            portableAccounts.Add(new ArchiveDomainPortableAccount(
                account.Id,
                identity,
                itemObservations,
                current.Select(item => item.Acquisition).ToArray(),
                revisions,
                challenges));
        }

        bool hasDetailedChallenges = ArchiveDomainPortablePackage.ContainsDetailedChallenges(portableAccounts);
        bool hasCharacterDetails = ArchiveDomainPortablePackage.ContainsCharacterDetails(portableAccounts);
        var package = new ArchiveDomainPortablePackage(
            datasetKind,
            FormatMajor: hasDetailedChallenges || hasCharacterDetails ? 2 : 1,
            FormatMinor: hasCharacterDetails ? (ArchiveDomainPortablePackage.ContainsCharacterEnhancements(portableAccounts) ? 1 : 0) : ArchiveDomainPortablePackage.ContainsTheaterBuffDetails(portableAccounts) ? 3 :
                ArchiveDomainPortablePackage.ContainsStygianDetails(portableAccounts) ? 2 :
                ArchiveDomainPortablePackage.ContainsTheaterDetails(portableAccounts) ? 1 : hasDetailedChallenges ? 0 : 1,
            PackageFingerprint: string.Empty,
            archive.Id,
            archive.Name,
            exportedAt,
            portableAccounts);
        package = package with
        {
            PackageFingerprint = ComputePackageFingerprint(package)
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new PortableWireDocument(FormatName, package),
            DisplayJson);
        if (payload.LongLength > MaximumFileBytes)
        {
            throw new InvalidDataException(
                $"The Portable dataset exceeds the {MaximumFileBytes}-byte limit.");
        }
        await destination.WriteAsync(payload, cancellationToken);
    }

    private static PortableManualAcquisitionRevision ToPortableRevision(
        ManualAcquisitionRevisionItem revision) => new(
            revision.RevisionId,
            revision.ChangeKind,
            revision.BeforeVersion,
            revision.AfterVersion,
            revision.Before,
            revision.After,
            revision.CreatedAt);

    public async Task<ArchiveDomainPortablePackage> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    $"The Portable dataset exceeds the {MaximumFileBytes}-byte limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        JsonNode root = JsonNode.Parse(buffer.ToArray()) ??
            throw new InvalidDataException(
                "The Portable dataset is empty or invalid.");
        JsonObject packageNode = root["package"]?.AsObject() ??
            throw new InvalidDataException(
                "The Portable dataset has no package payload.");
        string wireFingerprint = packageNode["package_fingerprint"]?
            .GetValue<string>() ?? string.Empty;
        packageNode["package_fingerprint"] = string.Empty;
        string wireExpected = ComputeNodeFingerprint(packageNode);
        if (!string.Equals(
                wireExpected,
                wireFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The Portable package fingerprint does not match its content. Expected {wireExpected}, received {wireFingerprint}.");
        }
        PortableWireDocument document =
            await JsonSerializer.DeserializeAsync<PortableWireDocument>(
                buffer,
                DisplayJson,
                cancellationToken) ?? throw new InvalidDataException(
                "The Portable dataset is empty or invalid.");
        if (document.Format != FormatName)
        {
            throw new InvalidDataException(
                "The Portable format name or major version is unsupported.");
        }
        ValidatePackage(document.Package);
        if (!string.Equals(
                wireFingerprint,
                document.Package.PackageFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Portable package fingerprint changed during decoding.");
        }
        return document.Package;
    }

    public async Task<ArchiveDomainPortablePlan> PlanAsync(
        ArchiveDomainPortablePackage package,
        Guid? targetArchiveId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidatePackage(package);
        PlayerArchive? targetArchive = targetArchiveId is null
            ? null
            : await archives.GetByIdAsync(
                targetArchiveId.Value,
                cancellationToken);
        bool createArchive = false;
        if (targetArchiveId is not null && targetArchive is null)
        {
            throw new KeyNotFoundException(
                "The selected target archive does not exist.");
        }
        if (targetArchive is null)
        {
            PlayerArchive[] exact = (await archives.GetAllAsync(
                    cancellationToken))
                .Where(item => item.Name == package.SourceArchiveName)
                .ToArray();
            if (exact.Length > 1)
            {
                return ConflictPlan(
                    package,
                    Guid.Empty,
                    "Multiple target archives have the exact source name; manual mapping is required.");
            }
            targetArchive = exact.SingleOrDefault();
            createArchive = targetArchive is null;
            targetArchiveId = targetArchive?.Id ?? Guid.NewGuid();
        }

        var mappings = new List<ArchiveDomainPortableAccountPlan>();
        var conflicts = new List<string>();
        int add = 0;
        int skip = 0;
        foreach (ArchiveDomainPortableAccount sourceAccount in package.Accounts)
        {
            GameRoleIdentity? byId = await accounts.GetRoleIdentityByIdAsync(
                sourceAccount.RoleIdentity.Id,
                cancellationToken);
            if (byId is not null && byId != sourceAccount.RoleIdentity)
            {
                conflicts.Add(
                    $"Role identity {sourceAccount.RoleIdentity.Id} maps to a different natural identity.");
                continue;
            }
            GameAccount? existing = createArchive
                ? null
                : await accounts.GetByArchiveIdAndNaturalIdentityAsync(
                    targetArchiveId!.Value,
                    sourceAccount.RoleIdentity.NaturalIdentity,
                    cancellationToken);
            Guid targetAccountId = existing?.Id ?? Guid.NewGuid();
            mappings.Add(new ArchiveDomainPortableAccountPlan(
                sourceAccount.SourceAccountId,
                targetAccountId,
                existing?.RoleIdentity?.Id ?? sourceAccount.RoleIdentity.Id,
                CreateAccount: existing is null));
            (int accountAdd, int accountSkip, string[] accountConflicts) =
                await CompareTargetAsync(
                    sourceAccount,
                    targetAccountId,
                    existing is not null,
                    package.DatasetKind,
                    cancellationToken);
            add += accountAdd;
            skip += accountSkip;
            conflicts.AddRange(accountConflicts);
        }

        return new ArchiveDomainPortablePlan(
            package.DatasetKind,
            package.PackageFingerprint,
            targetArchiveId!.Value,
            createArchive,
            mappings,
            add,
            skip,
            conflicts.Count,
            conflicts);
    }

    public async Task ExportMarkdownAsync(
        Guid archiveId,
        ArchiveDomainDatasetKind datasetKind,
        TextWriter destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        PlayerArchive archive = await archives.GetByIdAsync(
            archiveId,
            cancellationToken) ?? throw new KeyNotFoundException(
                "The archive to export does not exist.");
        await destination.WriteLineAsync($"# {EscapeMarkdown(archive.Name)}");
        await destination.WriteLineAsync();
        await destination.WriteLineAsync(
            datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                ? "角色与武器历史（只读报告）"
                : "挑战历史（只读报告）");
        foreach (GameAccount account in await accounts.GetByArchiveIdAsync(
                     archiveId,
                     cancellationToken))
        {
            await destination.WriteLineAsync();
            await destination.WriteLineAsync($"## UID {account.Uid}");
            if (datasetKind ==
                ArchiveDomainDatasetKind.CharacterWeaponHistory)
            {
                IReadOnlyList<ItemStateObservation> observations =
                    await history.GetItemObservationsAsync(
                        account.Id,
                        cancellationToken: cancellationToken);
                foreach (ItemStateObservation observation in observations)
                {
                    await destination.WriteLineAsync(
                        $"- 状态观察 `{observation.ObservationId}`：" +
                        $"{observation.Kind}，集合完整度 {observation.CollectionCompleteness}，" +
                        $"观察时间 {FormatTime(observation.Provenance.Timestamps.ObservedAt)}，" +
                        $"抓取时间 {FormatTime(observation.Provenance.Timestamps.FetchedAt)}，" +
                        $"来源 {FormatSource(observation.Provenance)}，{observation.Entries.Count} 项");
                    foreach (InventoryStateEntry entry in observation.Entries)
                    {
                        await destination.WriteLineAsync(
                            $"  - `{EscapeMarkdown(entry.TypeId)}`：" +
                            $"实例 {FormatMissing(entry.InstanceId)}，" +
                            $"数量 {FormatMissing(entry.Quantity)}，" +
                            $"等级 {FormatMissing(entry.Level)}，" +
                            $"进度 {FormatMissing(entry.Progression)}，" +
                            $"字段 {entry.FieldCoverage} / {entry.FieldCompleteness}");
                    }
                }
                foreach (ManualAcquisitionState acquisition in
                         await history.GetManualAcquisitionsAsync(
                             account.Id,
                             cancellationToken: cancellationToken))
                {
                    await destination.WriteLineAsync(
                        $"- 手工获取 `{acquisition.Acquisition.AcquisitionId}`：" +
                        $"{EscapeMarkdown(acquisition.Acquisition.TypeId)}，" +
                        $"{FormatOccurrence(acquisition.Acquisition.OccurredAt)}，" +
                        $"原因 {acquisition.Acquisition.Reason}，" +
                        $"说明 {FormatMissing(acquisition.Acquisition.ReasonDetail)}，" +
                        $"版本 {acquisition.Version.Value}");
                }
                foreach (IGrouping<InventoryItemKind, ItemStateObservation>
                         group in observations.GroupBy(item => item.Kind))
                {
                    ItemStateObservation[] ordered = group
                        .OrderBy(item =>
                            item.Provenance.Timestamps.ObservedAt?
                                .UtcDateTime.Ticks ?? long.MinValue)
                        .ThenBy(item => item.ObservationId.Value)
                        .ToArray();
                    for (int index = 1; index < ordered.Length; index++)
                    {
                        IReadOnlyList<InventoryStateChange> changes =
                            ItemStateHistoryComparer.Compare(
                                ordered[index - 1],
                                ordered[index]);
                        await destination.WriteLineAsync(
                            $"- 派生变化 {group.Key}：" +
                            $"{FormatTime(ordered[index - 1].Provenance.Timestamps.ObservedAt)} 至 " +
                            $"{FormatTime(ordered[index].Provenance.Timestamps.ObservedAt)}，" +
                            $"{changes.Count} 项；变化时刻通常仅能确定在该区间内");
                        foreach (InventoryStateChange change in changes)
                        {
                            await destination.WriteLineAsync(
                                $"  - `{EscapeMarkdown(change.StableItemKey)}`：" +
                                $"{change.ChangeKind} / {change.Certainty}");
                        }
                    }
                }
            }
            else
            {
                foreach (ChallengeObservation observation in
                         await history.GetChallengeObservationsAsync(
                             account.Id,
                             cancellationToken: cancellationToken))
                {
                    await destination.WriteLineAsync(
                        $"- `{observation.SeasonId}` / `{observation.ObservationId}`：" +
                        $"{observation.Mode}，{observation.Completeness}，观察时间 " +
                        $"{FormatTime(observation.Provenance.Timestamps.ObservedAt)}，" +
                        $"周期 {FormatTime(observation.PeriodStart)} 至 " +
                        $"{FormatTime(observation.PeriodEnd)}，来源 " +
                        $"{FormatSource(observation.Provenance)}");
                    if (observation.SpiralAbyssStatistics is { } statistics)
                    {
                        if (statistics.TotalBattleTimes is int battles) await destination.WriteLineAsync($"  - 战斗次数：{battles}");
                        if (statistics.TotalWinTimes is int wins) await destination.WriteLineAsync($"  - 胜利次数：{wins}");
                        if (statistics.MaxFloor is { } maxFloor) await destination.WriteLineAsync($"  - 最深进度：{EscapeMarkdown(maxFloor)}");
                        if (statistics.TotalStars is int totalStars) await destination.WriteLineAsync($"  - 官方总星数：{totalStars}");
                        (string Name, IReadOnlyList<SpiralAbyssRankEntry> Entries)[] rankings =
                        [
                            ("出战次数", statistics.RevealRank), ("击破次数", statistics.DefeatRank),
                            ("最强一击", statistics.DamageRank), ("承受伤害", statistics.TakeDamageRank),
                            ("元素战技释放", statistics.NormalSkillRank), ("元素爆发释放", statistics.EnergySkillRank),
                        ];
                        foreach (var ranking in rankings.Where(ranking => ranking.Entries.Count > 0))
                            await destination.WriteLineAsync($"  - {ranking.Name}：" + string.Join("、",
                                ranking.Entries.Select(entry => $"角色 {EscapeMarkdown(entry.AvatarId)} = {entry.Value.ToString(CultureInfo.InvariantCulture)}")));
                    }
                    if (observation.TheaterStatistics is { } theaterStatistics)
                        await WriteTheaterSummaryAsync(destination, theaterStatistics);
                    foreach (SpiralAbyssFloorResult floor in
                             observation.SpiralAbyssFloors)
                    {
                        await destination.WriteLineAsync(
                            $"  - 第 {floor.Floor} 层：{floor.Stars} 星，" +
                            $"{floor.Completeness}");
                        foreach (SpiralAbyssChamberResult chamber in
                                 floor.Chambers)
                        {
                            await destination.WriteLineAsync(
                                $"    - 第 {chamber.Chamber} 间：" +
                                $"{chamber.Stars} 星，" +
                                $"完成时间 {FormatOccurrence(chamber.CompletedAt)}");
                            foreach (SpiralAbyssBattleResult battle in chamber.Battles)
                            {
                                string half = battle.Half == 1 ? "上半" : "下半";
                                string team = string.Join("、", battle.Participants.Select(item => EscapeMarkdown(item.Name ?? item.Id)));
                                string enemies = string.Join("、", battle.Enemies.Select(item => EscapeMarkdown(item.Name ?? "未知")));
                                await destination.WriteLineAsync(
                                    $"      - {half} · {FormatOccurrence(battle.CompletedAt)}" +
                                    (team.Length > 0 ? $" · 阵容 {team}" : string.Empty) +
                                    (enemies.Length > 0 ? $" · 敌人 {enemies}" : string.Empty));
                            }
                        }
                    }
                    if (observation.Mode == ChallengeMode.StygianOnslaught)
                    {
                        await WriteStygianDetailsAsync(destination, observation);
                        continue;
                    }
                    if (observation.Difficulty is not null ||
                        observation.MedalCount is not null)
                    {
                        await destination.WriteLineAsync(
                            $"  - 难度 {observation.Difficulty?.ToString() ?? "未知"}，" +
                            $"奖章 {observation.MedalCount?.ToString() ?? "未知"}");
                    }
                    foreach (ChallengeStageResult stage in observation.Stages)
                    {
                        await destination.WriteLineAsync(
                            $"  - `{EscapeMarkdown(stage.StageId)}`：" +
                            $"分数 {stage.Score?.ToString() ?? "未知"}，" +
                            $"完成时间 {FormatOccurrence(stage.CompletedAt)}");
                        if (stage.TheaterRound is { } round)
                        {
                            await destination.WriteLineAsync($"    - 塔罗标记：{round.IsTarot?.ToString() ?? "未知"}，序号：{round.TarotSerialNumber?.ToString() ?? "未知"}");
                            await WriteTheaterBuffsAsync(destination, round);
                        }
                        if (stage.Participants.Count > 0)
                        {
                            await destination.WriteLineAsync(
                                "    - 阵容：" + string.Join(
                                    "、",
                                    stage.Participants.Select(DescribeParticipant)));
                        }
                        if (stage.Buffs.Count > 0)
                        {
                            await destination.WriteLineAsync(
                                "    - 增益：" + string.Join(
                                    "、", stage.Buffs.Select(EscapeMarkdown)));
                        }
                        if (stage.Enemies.Count > 0)
                        {
                            await destination.WriteLineAsync(
                                "    - 敌人：" + string.Join(
                                    "、",
                                    stage.Enemies.Select(enemy =>
                                        enemy.Name is { } enemyName
                                            ? enemy.Level is int level
                                                ? $"{enemyName}(Lv.{level})"
                                                : enemyName
                                            : "未知")));
                        }
                    }
                }
            }
        }
    }

    private async Task<(int Add, int Skip, string[] Conflicts)>
        CompareTargetAsync(
            ArchiveDomainPortableAccount source,
            Guid targetAccountId,
            bool targetExists,
            ArchiveDomainDatasetKind datasetKind,
            CancellationToken cancellationToken)
    {
        int add = 0;
        int skip = 0;
        var conflicts = new List<string>();
        if (!targetExists)
        {
            return (
                datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                    ? source.ItemObservations.Count +
                        source.ManualAcquisitions.Count
                    : source.ChallengeObservations.Count,
                0,
                []);
        }

        if (datasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory)
        {
            Dictionary<ItemObservationId, ItemStateObservation> existingItems =
                (await history.GetItemObservationsAsync(
                    targetAccountId,
                    cancellationToken: cancellationToken))
                .ToDictionary(item => item.ObservationId);
            foreach (ItemStateObservation item in source.ItemObservations)
            {
                ItemStateObservation mapped = new(
                    item.ObservationId,
                    targetAccountId,
                    item.Kind,
                    item.CollectionCompleteness,
                    item.Entries,
                    item.Provenance, item.CharacterMetadata);
                CountComparison(
                    existingItems.TryGetValue(
                        item.ObservationId,
                        out ItemStateObservation? existing),
                    existing,
                    mapped,
                    $"item observation {item.ObservationId}",
                    ref add,
                    ref skip,
                    conflicts);
            }
            Dictionary<ManualAcquisitionId, ManualAcquisition> existingManual =
                (await history.GetManualAcquisitionsAsync(
                    targetAccountId,
                    cancellationToken: cancellationToken))
                .ToDictionary(
                    item => item.Acquisition.AcquisitionId,
                    item => item.Acquisition);
            foreach (ManualAcquisition acquisition in
                     source.ManualAcquisitions)
            {
                ManualAcquisition mapped = new(
                    acquisition.AcquisitionId,
                    targetAccountId,
                    acquisition.Kind,
                    acquisition.TypeId,
                    acquisition.OccurredAt,
                    acquisition.Reason,
                    acquisition.ReasonDetail,
                    acquisition.Provenance);
                CountComparison(
                    existingManual.TryGetValue(
                        acquisition.AcquisitionId,
                        out ManualAcquisition? existing),
                    existing,
                    mapped,
                    $"manual acquisition {acquisition.AcquisitionId}",
                    ref add,
                    ref skip,
                    conflicts);
            }
        }
        else
        {
            var existing = (await history.GetChallengeObservationsAsync(
                    targetAccountId,
                    cancellationToken: cancellationToken))
                .ToList();
            foreach (ChallengeObservation challenge in
                     source.ChallengeObservations)
            {
                ChallengeObservation mapped = new(
                    challenge.ObservationId,
                    targetAccountId,
                    challenge.Mode,
                    challenge.SeasonId,
                    challenge.PeriodStart,
                    challenge.PeriodEnd,
                    challenge.Completeness,
                    challenge.SpiralAbyssFloors,
                    challenge.Provenance,
                    challenge.Stages,
                    challenge.Difficulty,
                    challenge.MedalCount,
                    challenge.SpiralAbyssStatistics,
                    challenge.TheaterStatistics, challenge.StygianStatistics, challenge.MultiplayerStages);
                ChallengePlanDecision decision = ClassifyChallenge(
                    existing,
                    mapped);
                if (decision == ChallengePlanDecision.Add)
                {
                    add++;
                    existing.Add(mapped);
                }
                else if (decision == ChallengePlanDecision.Skip)
                {
                    skip++;
                }
                else
                {
                    conflicts.Add(
                        $"challenge observation {challenge.ObservationId} conflicts with target evidence.");
                }
            }
        }
        return (add, skip, conflicts.ToArray());
    }

    private static ChallengePlanDecision ClassifyChallenge(
        IReadOnlyCollection<ChallengeObservation> existing,
        ChallengeObservation proposed)
    {
        ChallengeObservation? sameId = existing.SingleOrDefault(item =>
            item.ObservationId == proposed.ObservationId);
        if (sameId is not null)
        {
            return ChallengeBusinessEquals(sameId, proposed)
                ? ChallengePlanDecision.Skip
                : ChallengePlanDecision.Conflict;
        }
        string? sourceRecordId = proposed.Provenance.Source?.SourceRecordId;
        if (!string.IsNullOrWhiteSpace(sourceRecordId))
        {
            ChallengeObservation? sameSource = existing.SingleOrDefault(item =>
                item.Mode == proposed.Mode &&
                item.SeasonId == proposed.SeasonId &&
                item.Provenance.Source?.SourceRecordId == sourceRecordId);
            if (sameSource is not null)
            {
                return ChallengeBusinessEquals(sameSource, proposed)
                    ? ChallengePlanDecision.Skip
                    : ChallengePlanDecision.Conflict;
            }
        }
        DateTimeOffset? observed = proposed.Provenance.Timestamps.ObservedAt;
        if (observed is null)
        {
            return string.IsNullOrWhiteSpace(sourceRecordId)
                ? ChallengePlanDecision.Conflict
                : ChallengePlanDecision.Add;
        }
        ChallengeObservation[] ordered = existing
            .Where(item =>
                item.Mode == proposed.Mode &&
                item.SeasonId == proposed.SeasonId &&
                item.Provenance.Timestamps.ObservedAt is not null)
            .OrderBy(item => item.Provenance.Timestamps.ObservedAt)
            .ThenBy(item => item.ObservationId.Value)
            .ToArray();
        ChallengeObservation[] sameTime = ordered.Where(item =>
            item.Provenance.Timestamps.ObservedAt!.Value.UtcDateTime ==
            observed.Value.UtcDateTime).ToArray();
        if (sameTime.Length > 0)
        {
            return sameTime.All(item => ChallengeBusinessEquals(item, proposed))
                ? ChallengePlanDecision.Skip
                : ChallengePlanDecision.Conflict;
        }
        ChallengeObservation? previous = ordered.LastOrDefault(item =>
            item.Provenance.Timestamps.ObservedAt!.Value.UtcDateTime <
            observed.Value.UtcDateTime);
        ChallengeObservation? next = ordered.FirstOrDefault(item =>
            item.Provenance.Timestamps.ObservedAt!.Value.UtcDateTime >
            observed.Value.UtcDateTime);
        return new[] { previous, next }
            .Where(item => item is not null)
            .Any(item => ChallengeBusinessEquals(item!, proposed))
                ? ChallengePlanDecision.Skip
                : ChallengePlanDecision.Add;
    }

    private static bool ChallengeBusinessEquals(
        ChallengeObservation left,
        ChallengeObservation right) =>
        left.Mode == right.Mode &&
        left.SeasonId == right.SeasonId &&
        left.PeriodStart == right.PeriodStart &&
        left.PeriodEnd == right.PeriodEnd &&
        left.Completeness == right.Completeness &&
        left.Difficulty == right.Difficulty &&
        left.MedalCount == right.MedalCount &&
        JsonSerializer.Serialize(left.SpiralAbyssFloors, CompactJson) ==
        JsonSerializer.Serialize(right.SpiralAbyssFloors, CompactJson) &&
        JsonSerializer.Serialize(left.Stages, CompactJson) ==
        JsonSerializer.Serialize(right.Stages, CompactJson) &&
        JsonSerializer.Serialize(left.SpiralAbyssStatistics, CompactJson) ==
        JsonSerializer.Serialize(right.SpiralAbyssStatistics, CompactJson) &&
        JsonSerializer.Serialize(left.TheaterStatistics, CompactJson) ==
        JsonSerializer.Serialize(right.TheaterStatistics, CompactJson) &&
        JsonSerializer.Serialize(left.StygianStatistics, CompactJson) ==
        JsonSerializer.Serialize(right.StygianStatistics, CompactJson) &&
        JsonSerializer.Serialize(left.MultiplayerStages, CompactJson) ==
        JsonSerializer.Serialize(right.MultiplayerStages, CompactJson);

    private enum ChallengePlanDecision
    {
        Add,
        Skip,
        Conflict,
    }

    private static void CountComparison<T>(
        bool exists,
        T? existing,
        T proposed,
        string label,
        ref int add,
        ref int skip,
        ICollection<string> conflicts)
    {
        if (!exists)
        {
            add++;
            return;
        }
        string existingJson = JsonSerializer.Serialize(existing, CompactJson);
        string proposedJson = JsonSerializer.Serialize(proposed, CompactJson);
        if (existingJson == proposedJson)
        {
            skip++;
        }
        else
        {
            conflicts.Add($"{label} has different target content.");
        }
    }

    private static ArchiveDomainPortablePlan ConflictPlan(
        ArchiveDomainPortablePackage package,
        Guid targetArchiveId,
        string conflict) =>
        new(
            package.DatasetKind,
            package.PackageFingerprint,
            targetArchiveId,
            CreateArchive: false,
            Accounts: [],
            AddCount: 0,
            SkipCount: 0,
            ConflictCount: 1,
            Conflicts: [conflict]);

    private static void ValidatePackage(
        ArchiveDomainPortablePackage package)
    {
        if (!Enum.IsDefined(package.DatasetKind) ||
            !package.SupportsVersion() ||
            package.SourceArchiveId == Guid.Empty ||
            string.IsNullOrEmpty(package.SourceArchiveName))
        {
            throw new InvalidDataException(
                "The Portable package header is invalid.");
        }
        if (package.Accounts.Select(item => item.SourceAccountId)
            .Distinct()
            .Count() != package.Accounts.Count ||
            package.Accounts.Any(item =>
                item.SourceAccountId == Guid.Empty ||
                item.RoleIdentity is null))
        {
            throw new InvalidDataException(
                "The Portable package contains invalid or duplicate accounts.");
        }
        int factCount = package.Accounts.Sum(account =>
            package.DatasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                ? account.ItemObservations.Count +
                    account.ManualAcquisitions.Count +
                    account.ManualRevisions.Count
                : account.ChallengeObservations.Count);
        if (factCount > MaximumFactCount)
        {
            throw new InvalidDataException(
                $"The Portable dataset exceeds the {MaximumFactCount}-record limit.");
        }
        bool wrongDatasetPayload = package.Accounts.Any(account =>
            package.DatasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory
                ? account.ChallengeObservations.Count > 0
                : account.ItemObservations.Count > 0 ||
                  account.ManualAcquisitions.Count > 0 ||
                  account.ManualRevisions.Count > 0);
        if (wrongDatasetPayload)
        {
            throw new InvalidDataException(
                "The Portable package mixes independent dataset payloads.");
        }
    }

    private static string ComputePackageFingerprint(
        ArchiveDomainPortablePackage package)
    {
        ArchiveDomainPortablePackage normalized = package with
        {
            PackageFingerprint = string.Empty
        };
        JsonNode node = JsonSerializer.SerializeToNode(
            normalized,
            CompactJson) ?? throw new InvalidOperationException(
            "The Portable package could not be serialized.");
        return ComputeNodeFingerprint(node);
    }

    private static string ComputeNodeFingerprint(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalNode(writer, node);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
    }

    private static void WriteCanonicalNode(
        Utf8JsonWriter writer,
        JsonNode? node)
    {
        if (node is null)
        {
            writer.WriteNullValue();
            return;
        }
        if (node is JsonObject objectNode)
        {
            writer.WriteStartObject();
            foreach ((string name, JsonNode? value) in objectNode
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                WriteCanonicalNode(writer, value);
            }
            writer.WriteEndObject();
            return;
        }
        if (node is JsonArray arrayNode)
        {
            writer.WriteStartArray();
            foreach (JsonNode? item in arrayNode)
            {
                WriteCanonicalNode(writer, item);
            }
            writer.WriteEndArray();
            return;
        }
        node.WriteTo(writer, CompactJson);
    }

    internal static JsonSerializerOptions CreatePortableJsonOptions(
        bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented,
            MaxDepth = 64,
        };
        options.Converters.Add(new OccurrenceTimeConverter());
        options.Converters.Add(new ItemObservationIdConverter());
        options.Converters.Add(new ManualAcquisitionIdConverter());
        options.Converters.Add(new ChallengeObservationIdConverter());
        options.Converters.Add(new AcquisitionBatchIdConverter());
        options.Converters.Add(new FactVersionConverter());
        options.Converters.Add(new GameRoleIdentityConverter());
        options.Converters.Add(new ItemStateObservationConverter());
        options.Converters.Add(new ChallengeObservationConverter());
        return options;
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        ?? "未知";

    private static string FormatOccurrence(OccurrenceTime value) =>
        value.Kind == OccurrenceTimeKind.Unknown
            ? "发生时间未知"
            : $"{value.Kind} {FormatTime(value.Start)}" +
              (value.End is null ? string.Empty : $" 至 {FormatTime(value.End)}") +
              $"（精度 {value.Precision}）";

    private static string FormatMissing(object? value) =>
        value?.ToString() ?? "未提供";

    private static string FormatSource(RecordProvenance provenance) =>
        $"{provenance.Origin}" +
        (provenance.Source is null
            ? string.Empty
            : $" / {EscapeMarkdown(provenance.Source.Provider ?? "未知提供方")}" +
              $" / {EscapeMarkdown(provenance.Source.SourceRecordId ?? provenance.Source.SourceSnapshotId ?? "无引用")}");

    private sealed record PortableWireDocument(
        string Format,
        ArchiveDomainPortablePackage Package);

    private abstract class GuidValueConverter<T> : JsonConverter<T>
    {
        protected abstract T Create(Guid value);

        protected abstract Guid GetValue(T value);

        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            Create(reader.GetGuid());

        public override void Write(
            Utf8JsonWriter writer,
            T value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(GetValue(value));
    }

    private sealed class ItemObservationIdConverter
        : GuidValueConverter<ItemObservationId>
    {
        protected override ItemObservationId Create(Guid value) => new(value);
        protected override Guid GetValue(ItemObservationId value) => value.Value;
    }

    private sealed class ManualAcquisitionIdConverter
        : GuidValueConverter<ManualAcquisitionId>
    {
        protected override ManualAcquisitionId Create(Guid value) => new(value);
        protected override Guid GetValue(ManualAcquisitionId value) => value.Value;
    }

    private sealed class ChallengeObservationIdConverter
        : GuidValueConverter<ChallengeObservationId>
    {
        protected override ChallengeObservationId Create(Guid value) => new(value);
        protected override Guid GetValue(ChallengeObservationId value) => value.Value;
    }

    private sealed class AcquisitionBatchIdConverter
        : GuidValueConverter<AcquisitionBatchId>
    {
        protected override AcquisitionBatchId Create(Guid value) => new(value);
        protected override Guid GetValue(AcquisitionBatchId value) => value.Value;
    }

    private sealed class FactVersionConverter : JsonConverter<FactVersion>
    {
        public override FactVersion Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            new(reader.GetInt64());

        public override void Write(
            Utf8JsonWriter writer,
            FactVersion value,
            JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private sealed record ItemObservationWire(
        ItemObservationId ObservationId,
        Guid GameAccountId,
        InventoryItemKind Kind,
        DataCompleteness CollectionCompleteness,
        IReadOnlyList<InventoryStateEntry> Entries,
        RecordProvenance Provenance,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CharacterResponseMetadata? CharacterMetadata = null);

    private sealed record GameRoleIdentityWire(
        Guid Id,
        GameRoleNaturalIdentity NaturalIdentity);

    private sealed class GameRoleIdentityConverter
        : JsonConverter<GameRoleIdentity>
    {
        public override GameRoleIdentity Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            GameRoleIdentityWire wire =
                JsonSerializer.Deserialize<GameRoleIdentityWire>(
                    ref reader,
                    options) ?? throw new JsonException(
                    "A game role identity payload is invalid.");
            return new GameRoleIdentity(
                new GameRoleIdentityId(wire.Id),
                wire.NaturalIdentity);
        }

        public override void Write(
            Utf8JsonWriter writer,
            GameRoleIdentity value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(
                writer,
                new GameRoleIdentityWire(
                    value.Id.Value,
                    value.NaturalIdentity),
                options);
    }

    private sealed record ChallengeObservationWire(
        ChallengeObservationId ObservationId,
        Guid GameAccountId,
        ChallengeMode Mode,
        string SeasonId,
        DateTimeOffset? PeriodStart,
        DateTimeOffset? PeriodEnd,
        DataCompleteness Completeness,
        IReadOnlyList<SpiralAbyssFloorWire> SpiralAbyssFloors,
        RecordProvenance Provenance,
        IReadOnlyList<ChallengeStageWire>? Stages = null,
        int? Difficulty = null,
        int? MedalCount = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SpiralAbyssStatisticsWire? SpiralAbyssStatistics = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TheaterStatisticsWire? TheaterStatistics = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        StygianStatisticsWire? StygianStatistics = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ChallengeStageWire>? MultiplayerStages = null);

    private sealed record TheaterStatisticsWire(
        int? MaxRoundId, int? Heraldry, int? CoinCount, int? AvatarBonusCount, int? RentCount,
        int? TarotFinishedCount, int? TotalUseTime, bool? ShowBattleStats, int[]? MedalRounds,
        ChallengeParticipantWire[]? BackupAvatars,
        TheaterAvatarStatisticWire? MaxDefeat, TheaterAvatarStatisticWire? MaxDamage,
        TheaterAvatarStatisticWire? MaxTakeDamage, string? TotalCoinsConsumed,
        TheaterAvatarStatisticWire[]? ShortestAvatars)
    {
        public static TheaterStatisticsWire FromDomain(TheaterStatistics value) =>
            new(value.MaxRoundId, value.Heraldry, value.CoinCount, value.AvatarBonusCount,
                value.RentCount, value.TarotFinishedCount, value.TotalUseTime, value.ShowBattleStats,
                value.MedalRounds.ToArray(), value.BackupAvatars.Select(ChallengeParticipantWire.FromDomain).ToArray(),
                TheaterAvatarStatisticWire.FromDomain(value.MaxDefeat),
                TheaterAvatarStatisticWire.FromDomain(value.MaxDamage),
                TheaterAvatarStatisticWire.FromDomain(value.MaxTakeDamage), value.TotalCoinsConsumed,
                value.ShortestAvatars.Select(item => TheaterAvatarStatisticWire.FromDomain(item)!).ToArray());

        public TheaterStatistics ToDomain() =>
            new(MaxRoundId, Heraldry, CoinCount, AvatarBonusCount, RentCount, TarotFinishedCount,
                TotalUseTime, ShowBattleStats, MedalRounds,
                (BackupAvatars ?? []).Select(ChallengeParticipantWire.ToDomainChecked).ToArray(),
                MaxDefeat?.ToDomain(), MaxDamage?.ToDomain(), MaxTakeDamage?.ToDomain(),
                TotalCoinsConsumed, (ShortestAvatars ?? []).Select(item => item?.ToDomain()
                    ?? throw new InvalidDataException("A Theater statistic cannot be null.")).ToArray());
    }

    private sealed record TheaterAvatarStatisticWire(ChallengeParticipantWire Participant, string? Value)
    {
        public static TheaterAvatarStatisticWire? FromDomain(TheaterAvatarStatistic? value) =>
            value is null ? null : new(ChallengeParticipantWire.FromDomain(value.Participant), value.Value);
        public TheaterAvatarStatistic ToDomain() => new(ChallengeParticipantWire.ToDomainChecked(Participant), Value);
    }

    private sealed record TheaterRoundWire(bool? IsTarot, int? TarotSerialNumber,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TheaterBuffWire[]? BuffDetails = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BuffSummary = null)
    {
        public static TheaterRoundWire FromDomain(TheaterRoundDetails value) => new(
            value.IsTarot, value.TarotSerialNumber, value.BuffDetails?.Select(TheaterBuffWire.FromDomain).ToArray(), value.BuffSummary);
        public TheaterRoundDetails ToDomain() => new(IsTarot, TarotSerialNumber,
            BuffDetails?.Select(value => value?.ToDomain() ?? throw new InvalidDataException("A Theater buff cannot be null.")).ToArray(),
            BuffSummary);
    }

    private sealed record TheaterBuffWire(string Kind, string Name, long? SourceId, string? Description,
        string? IconUrl, int? Level, bool? IsEnhanced, IReadOnlyList<string>? Effects)
    {
        public static TheaterBuffWire FromDomain(TheaterBuff value) => new(value.Kind switch
        {
            TheaterBuffKind.ChoiceCard => "choice_card", TheaterBuffKind.Buff => "buff",
            TheaterBuffKind.Splendour => "splendour", _ => throw new InvalidDataException("Unknown Theater buff kind."),
        }, value.Name, value.SourceId, value.Description, value.IconUrl, value.Level, value.IsEnhanced, value.Effects);
        public TheaterBuff ToDomain() => new(Kind switch
        {
            "choice_card" => TheaterBuffKind.ChoiceCard, "buff" => TheaterBuffKind.Buff,
            "splendour" => TheaterBuffKind.Splendour, _ => throw new InvalidDataException("Unknown Theater buff kind."),
        }, Name, SourceId, Description, IconUrl, Level, IsEnhanced, Effects);
    }

    private sealed record SpiralAbyssStatisticsWire(
        int? TotalBattleTimes, int? TotalWinTimes, string? MaxFloor, int? TotalStars,
        SpiralAbyssRankWire[]? RevealRank, SpiralAbyssRankWire[]? DefeatRank,
        SpiralAbyssRankWire[]? DamageRank, SpiralAbyssRankWire[]? TakeDamageRank,
        SpiralAbyssRankWire[]? NormalSkillRank, SpiralAbyssRankWire[]? EnergySkillRank)
    {
        public static SpiralAbyssStatisticsWire FromDomain(SpiralAbyssStatistics value) =>
            new(value.TotalBattleTimes, value.TotalWinTimes, value.MaxFloor, value.TotalStars,
                ToWire(value.RevealRank), ToWire(value.DefeatRank), ToWire(value.DamageRank),
                ToWire(value.TakeDamageRank), ToWire(value.NormalSkillRank), ToWire(value.EnergySkillRank));

        public SpiralAbyssStatistics ToDomain() =>
            new(TotalBattleTimes, TotalWinTimes, MaxFloor, TotalStars,
                ToDomain(RevealRank), ToDomain(DefeatRank), ToDomain(DamageRank),
                ToDomain(TakeDamageRank), ToDomain(NormalSkillRank), ToDomain(EnergySkillRank));

        private static SpiralAbyssRankWire[] ToWire(IReadOnlyList<SpiralAbyssRankEntry> values) =>
            values.Select(value => new SpiralAbyssRankWire(
                value.AvatarId, value.Value, value.IconUrl, value.Rarity)).ToArray();

        private static SpiralAbyssRankEntry[] ToDomain(SpiralAbyssRankWire[]? values) =>
            (values ?? []).Select(value => value is null
                ? throw new InvalidDataException("An Abyss ranking entry cannot be null.")
                : new SpiralAbyssRankEntry(value.AvatarId, value.Value, value.IconUrl, value.Rarity)).ToArray();
    }

    private sealed record SpiralAbyssRankWire(string AvatarId, long Value, string? IconUrl, int? Rarity);

    private sealed record ChallengeStageWire(
        string StageId,
        int? Score,
        OccurrenceTime CompletedAt,
        IReadOnlyList<ChallengeParticipantWire>? Participants = null,
        IReadOnlyList<string>? Buffs = null,
        IReadOnlyList<ChallengeEnemyWire>? Enemies = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TheaterRoundWire? TheaterRound = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        StygianStageWire? StygianDetails = null)
    {
        public static ChallengeStageWire FromDomain(ChallengeStageResult stage) =>
            new(stage.StageId, stage.Score, stage.CompletedAt,
                stage.Participants.Select(ChallengeParticipantWire.FromDomain).ToArray(), stage.Buffs.ToArray(),
                stage.Enemies.Select(enemy => new ChallengeEnemyWire(enemy.Name, enemy.Level, enemy.IconUrl)).ToArray(),
                stage.TheaterRound is { } round ? TheaterRoundWire.FromDomain(round) : null,
                stage.StygianDetails is { } details ? StygianStageWire.FromDomain(details) : null);

        public static ChallengeStageResult ToDomainChecked(ChallengeStageWire? stage) =>
            stage is null ? throw new InvalidDataException("A challenge stage cannot be null.") :
            new(stage.StageId, stage.Score, stage.CompletedAt,
                (stage.Participants ?? []).Select(ChallengeParticipantWire.ToDomainChecked), stage.Buffs,
                (stage.Enemies ?? []).Select(enemy => enemy is null ? throw new InvalidDataException("An enemy cannot be null.") :
                    new ChallengeEnemy(enemy.Name, enemy.Level, enemy.IconUrl)),
                stage.TheaterRound?.ToDomain(), stage.StygianDetails?.ToDomain());
    }

    private sealed record ChallengeParticipantWire(
        string Id,
        string? Name,
        string? IconUrl,
        int? Level,
        int? Rarity,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ChallengeParticipantOrigin? Origin = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? Constellation = null)
    {
        public static ChallengeParticipantWire FromDomain(ChallengeParticipant value) =>
            new(value.Id, value.Name, value.IconUrl, value.Level, value.Rarity, value.Origin, value.Constellation);
        public static ChallengeParticipant ToDomainChecked(ChallengeParticipantWire? value) =>
            value is null ? throw new InvalidDataException("A challenge participant cannot be null.") :
                new(value.Id, value.Name, value.IconUrl, value.Level, value.Rarity, value.Origin, value.Constellation);
    }

    private sealed record ChallengeEnemyWire(
        string? Name,
        int? Level,
        string? IconUrl);

    private sealed record SpiralAbyssFloorWire(
        int Floor,
        DataCompleteness Completeness,
        IReadOnlyList<SpiralAbyssChamberWire> Chambers);

    private sealed record SpiralAbyssChamberWire(
        int Chamber,
        int Stars,
        OccurrenceTime CompletedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SpiralAbyssBattleWire[]? Battles = null);

    private sealed record SpiralAbyssBattleWire(
        int Half,
        OccurrenceTime CompletedAt,
        ChallengeParticipantWire[] Participants,
        ChallengeEnemyWire[] Enemies)
    {
        public static SpiralAbyssBattleWire FromDomain(SpiralAbyssBattleResult battle) =>
            new(battle.Half, battle.CompletedAt,
                battle.Participants.Select(item =>
                    ChallengeParticipantWire.FromDomain(item)).ToArray(),
                battle.Enemies.Select(item =>
                    new ChallengeEnemyWire(item.Name, item.Level, item.IconUrl)).ToArray());

        public SpiralAbyssBattleResult ToDomain() =>
            new(Half, CompletedAt,
                Participants.Select(item =>
                    ChallengeParticipantWire.ToDomainChecked(item)),
                Enemies.Select(item => new ChallengeEnemy(item.Name, item.Level, item.IconUrl)));
    }

    private sealed class ItemStateObservationConverter
        : JsonConverter<ItemStateObservation>
    {
        public override ItemStateObservation Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            ItemObservationWire wire =
                JsonSerializer.Deserialize<ItemObservationWire>(
                    ref reader,
                    options) ?? throw new JsonException(
                    "An item observation payload is invalid.");
            return new ItemStateObservation(
                wire.ObservationId,
                wire.GameAccountId,
                wire.Kind,
                wire.CollectionCompleteness,
                wire.Entries,
                wire.Provenance, wire.CharacterMetadata);
        }

        public override void Write(
            Utf8JsonWriter writer,
            ItemStateObservation value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(
                writer,
                new ItemObservationWire(
                    value.ObservationId,
                    value.GameAccountId,
                    value.Kind,
                    value.CollectionCompleteness,
                    value.Entries,
                    value.Provenance, value.CharacterMetadata),
                options);
    }

    private sealed class ChallengeObservationConverter
        : JsonConverter<ChallengeObservation>
    {
        public override ChallengeObservation Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            ChallengeObservationWire wire =
                JsonSerializer.Deserialize<ChallengeObservationWire>(
                    ref reader,
                    options) ?? throw new JsonException(
                    "A challenge observation payload is invalid.");
            return new ChallengeObservation(
                wire.ObservationId,
                wire.GameAccountId,
                wire.Mode,
                wire.SeasonId,
                wire.PeriodStart,
                wire.PeriodEnd,
                wire.Completeness,
                wire.SpiralAbyssFloors.Select(floor =>
                    new SpiralAbyssFloorResult(
                        floor.Floor,
                        floor.Completeness,
                        floor.Chambers.Select(chamber =>
                            new SpiralAbyssChamberResult(
                                chamber.Chamber,
                                chamber.Stars,
                                chamber.CompletedAt,
                                chamber.Battles?.Select(battle => battle.ToDomain()))))),
                wire.Provenance,
                (wire.Stages ?? []).Select(ChallengeStageWire.ToDomainChecked),
                wire.Difficulty,
                wire.MedalCount,
                wire.SpiralAbyssStatistics?.ToDomain(),
                wire.TheaterStatistics?.ToDomain(), wire.StygianStatistics?.ToDomain(),
                (wire.MultiplayerStages ?? []).Select(ChallengeStageWire.ToDomainChecked));
        }

        public override void Write(
            Utf8JsonWriter writer,
            ChallengeObservation value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(
                writer,
                new ChallengeObservationWire(
                    value.ObservationId,
                    value.GameAccountId,
                    value.Mode,
                    value.SeasonId,
                    value.PeriodStart,
                    value.PeriodEnd,
                    value.Completeness,
                    value.SpiralAbyssFloors.Select(floor =>
                        new SpiralAbyssFloorWire(
                            floor.Floor,
                            floor.Completeness,
                            floor.Chambers.Select(chamber =>
                                new SpiralAbyssChamberWire(
                                    chamber.Chamber,
                                    chamber.Stars,
                                    chamber.CompletedAt,
                                    chamber.Battles.Count == 0 ? null :
                                        chamber.Battles.Select(SpiralAbyssBattleWire.FromDomain).ToArray())).ToArray()))
                        .ToArray(),
                    value.Provenance,
                    value.Stages.Select(ChallengeStageWire.FromDomain).ToArray(),
                    value.Difficulty,
                    value.MedalCount,
                    value.SpiralAbyssStatistics is { } statistics
                        ? SpiralAbyssStatisticsWire.FromDomain(statistics) : null,
                    value.TheaterStatistics is { } theaterStatistics
                        ? TheaterStatisticsWire.FromDomain(theaterStatistics) : null,
                    value.StygianStatistics is { } stygian ? StygianStatisticsWire.FromDomain(stygian) : null,
                    value.MultiplayerStages.Count == 0 ? null : value.MultiplayerStages.Select(ChallengeStageWire.FromDomain).ToArray()),
                options);
    }

    private sealed class OccurrenceTimeConverter
        : JsonConverter<OccurrenceTime>
    {
        public override OccurrenceTime Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            OccurrenceTimeKind kind = (OccurrenceTimeKind)root
                .GetProperty("kind")
                .GetInt32();
            if (kind == OccurrenceTimeKind.Unknown)
            {
                return OccurrenceTime.Unknown;
            }
            DateTimeOffset start = root.GetProperty("start")
                .GetDateTimeOffset();
            TimePrecision precision = (TimePrecision)root
                .GetProperty("precision")
                .GetInt32();
            return kind switch
            {
                OccurrenceTimeKind.Instant =>
                    OccurrenceTime.At(start, precision),
                OccurrenceTimeKind.Interval =>
                    OccurrenceTime.ForInterval(
                        start,
                        root.GetProperty("end").GetDateTimeOffset(),
                        precision),
                OccurrenceTimeKind.UncertainInterval =>
                    OccurrenceTime.Within(
                        start,
                        root.GetProperty("end").GetDateTimeOffset(),
                        precision),
                _ => throw new JsonException(
                    "The occurrence time kind is unsupported."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            OccurrenceTime value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("kind", (int)value.Kind);
            if (value.Start is not null)
            {
                writer.WriteString("start", value.Start.Value);
            }
            if (value.End is not null)
            {
                writer.WriteString("end", value.End.Value);
            }
            if (value.Precision is not null)
            {
                writer.WriteNumber("precision", (int)value.Precision.Value);
            }
            writer.WriteEndObject();
        }
    }
}
