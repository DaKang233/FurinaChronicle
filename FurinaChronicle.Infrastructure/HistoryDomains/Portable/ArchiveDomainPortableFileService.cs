// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

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

public sealed class ArchiveDomainPortableFileService(
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

        var package = new ArchiveDomainPortablePackage(
            datasetKind,
            FormatMajor: 1,
            FormatMinor: 0,
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
        if (document.Format != FormatName || document.Package.FormatMajor != 1)
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
                        $"{observation.Completeness}，观察时间 " +
                        $"{FormatTime(observation.Provenance.Timestamps.ObservedAt)}，" +
                        $"周期 {FormatTime(observation.PeriodStart)} 至 " +
                        $"{FormatTime(observation.PeriodEnd)}，来源 " +
                        $"{FormatSource(observation.Provenance)}");
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
                    item.Provenance);
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
                    challenge.Provenance);
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
        JsonSerializer.Serialize(left.SpiralAbyssFloors, CompactJson) ==
        JsonSerializer.Serialize(right.SpiralAbyssFloors, CompactJson);

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
            package.FormatMajor != 1 || package.FormatMinor < 0 ||
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
        RecordProvenance Provenance);

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
        RecordProvenance Provenance);

    private sealed record SpiralAbyssFloorWire(
        int Floor,
        DataCompleteness Completeness,
        IReadOnlyList<SpiralAbyssChamberWire> Chambers);

    private sealed record SpiralAbyssChamberWire(
        int Chamber,
        int Stars,
        OccurrenceTime CompletedAt);

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
                wire.Provenance);
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
                    value.Provenance),
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
                                chamber.CompletedAt)))),
                wire.Provenance);
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
                                    chamber.CompletedAt)).ToArray()))
                        .ToArray(),
                    value.Provenance),
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
