// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private PreparedArchiveRestore PrepareArchiveRestore(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        IReadOnlyList<MaterializedArchiveFact> materialized)
    {
        PreparedArchiveParents parents = PrepareArchiveParents(
            connection,
            request);
        var gacha = new List<PreparedMutation>();
        var items = new List<PreparedDomainMutation>();
        var manuals = new List<PreparedDomainMutation>();
        var challenges = new List<PreparedDomainMutation>();
        var revisions = new List<MaterializedArchiveFact>();
        HashSet<(Guid, string)> sourceGachaRevisionFacts = materialized
            .Where(item => item.Plan.Kind ==
                ArchiveRestoreFactKind.GachaRevision)
            .Select(item => (
                item.Plan.SourceAccountReference,
                item.Plan.TombstoneReference))
            .ToHashSet();
        HashSet<(Guid, string)> sourceManualRevisionFacts = materialized
            .Where(item => item.Plan.Kind ==
                ArchiveRestoreFactKind.ManualAcquisitionRevision)
            .Select(item => (
                item.Plan.SourceAccountReference,
                item.Plan.TombstoneReference))
            .ToHashSet();
        var mappedGachaRevisionFacts = new HashSet<(Guid, string)>();
        var mappedManualRevisionFacts = new HashSet<(Guid, string)>();

        foreach (MaterializedArchiveFact item in materialized)
        {
            Guid targetAccountId =
                parents.TargetAccounts[item.Plan.SourceAccountReference];
            Guid targetArchiveId =
                parents.TargetArchives[item.Plan.SourceArchiveReference];
            Guid? targetRoleId =
                parents.TargetRoleIds[item.Plan.SourceAccountReference];
            switch (item.Plan.Kind)
            {
                case ArchiveRestoreFactKind.GachaCurrent:
                    PrepareArchiveGacha(
                        connection,
                        item,
                        targetAccountId,
                        targetArchiveId,
                        targetRoleId,
                        sourceGachaRevisionFacts,
                        mappedGachaRevisionFacts,
                        gacha);
                    break;
                case ArchiveRestoreFactKind.ItemObservation:
                    items.Add(PreparedDomainMutation.Insert(
                        targetArchiveId,
                        CreateArchiveItemRow(
                            connection,
                            (ItemStateObservation)item.Value,
                            targetAccountId,
                            targetArchiveId,
                            targetRoleId)));
                    break;
                case ArchiveRestoreFactKind.ManualAcquisition:
                    PrepareArchiveManual(
                        connection,
                        item,
                        targetAccountId,
                        targetArchiveId,
                        targetRoleId,
                        sourceManualRevisionFacts,
                        mappedManualRevisionFacts,
                        manuals);
                    break;
                case ArchiveRestoreFactKind.ChallengeObservation:
                    challenges.Add(PreparedDomainMutation.Insert(
                        targetArchiveId,
                        CreateArchiveChallengeRow(
                            connection,
                            (ChallengeObservation)item.Value,
                            targetAccountId,
                            targetArchiveId,
                            targetRoleId)));
                    break;
                case ArchiveRestoreFactKind.GachaRevision:
                case ArchiveRestoreFactKind.ManualAcquisitionRevision:
                    revisions.Add(item);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        ValidateArchiveSourceRevisions(connection, revisions);
        ArchiveRestoreTombstoneHit[] tombstones = request.Plan.Tombstones
            .DistinctBy(item => (item.TombstoneKey, item.TombstoneVersion))
            .ToArray();
        ValidateArchiveTombstones(connection, tombstones);

        Guid[] affectedArchives = parents.TargetArchives.Values
            .Distinct()
            .ToArray();
        bool settingsChange = request.Plan.Settings.Differences.Any(item =>
            item.WillApply && item.TargetValue != item.SourceValue);
        bool infrastructureChange = parents.ArchivesToInsert.Count > 0 ||
            parents.AccountsToInsert.Count > 0 ||
            parents.AliasesToInsert.Count > 0 || revisions.Count > 0 ||
            tombstones.Length > 0 || settingsChange;
        bool undoCandidate = request.Plan.Undo.IsEligibleCandidate &&
            !infrastructureChange;
        bool undoEligible = undoCandidate &&
            parents.NewArchiveUndoSettings.Count == 0 &&
            affectedArchives.All(id => ReadUndoLimit(connection, id) > 0);
        int factCount = gacha.Count + items.Count + manuals.Count +
            challenges.Count + revisions.Count;
        int infrastructureCount = parents.ArchivesToInsert.Count +
            parents.AccountsToInsert.Count +
            parents.IdentitiesToInsert.Count +
            parents.AliasesToInsert.Count + tombstones.Length +
            (settingsChange ? 1 : 0);
        long snapshotBytes = gacha.Sum(item => ArchiveUtf8Bytes(
                SerializeSnapshot(item.After))) +
            items.Concat(manuals).Concat(challenges)
                .Sum(item => ArchiveUtf8Bytes(item.AfterSnapshotJson)) +
            revisions.Sum(item => EstimateArchiveRevisionBytes(item.Value));

        return new PreparedArchiveRestore(
            parents.TargetArchives,
            parents.TargetAccounts,
            parents.TargetRoleIds,
            affectedArchives,
            parents.ArchivesToInsert,
            parents.IdentitiesToInsert,
            parents.AccountsToInsert,
            parents.AliasesToInsert,
            parents.NewArchiveUndoSettings,
            gacha,
            items,
            manuals,
            challenges,
            revisions,
            mappedGachaRevisionFacts,
            mappedManualRevisionFacts,
            tombstones,
            undoCandidate,
            undoEligible,
            factCount,
            checked(factCount + infrastructureCount),
            snapshotBytes,
            factCount == 0 && infrastructureCount == 0);
    }

    private static void PrepareArchiveGacha(
        SQLiteConnection connection,
        MaterializedArchiveFact item,
        Guid targetAccountId,
        Guid targetArchiveId,
        Guid? targetRoleId,
        IReadOnlySet<(Guid, string)> sourceRevisionFacts,
        ISet<(Guid, string)> mappedRevisionFacts,
        ICollection<PreparedMutation> mutations)
    {
        var mapped = ((GachaRecord)item.Value) with
        {
            GameAccountId = targetAccountId,
        };
        var reference = new GachaFactReference(
            targetAccountId,
            mapped.ExternalRecordId);
        if (ReadCurrent(connection, reference) is not null)
        {
            throw new ArchiveRestoreApplyConflictException(
                "A planned Gacha fact now exists in the target.");
        }
        GachaRecordRow row = GachaRecordRow.FromDomain(mapped);
        row.Version = NextFactVersion(
            connection,
            reference,
            targetArchiveId,
            targetRoleId is null
                ? null
                : new GameRoleIdentityId(targetRoleId.Value));
        mutations.Add(new PreparedMutation(
            targetArchiveId,
            Before: null,
            row,
            GachaRecordChangeKind.Substantive,
            EntityChangeKind.Insert));
        if (sourceRevisionFacts.Contains((
                item.Plan.SourceAccountReference,
                item.Plan.StableReference)))
        {
            mappedRevisionFacts.Add((
                targetAccountId,
                item.Plan.StableReference));
        }
    }

    private static void PrepareArchiveManual(
        SQLiteConnection connection,
        MaterializedArchiveFact item,
        Guid targetAccountId,
        Guid targetArchiveId,
        Guid? targetRoleId,
        IReadOnlySet<(Guid, string)> sourceRevisionFacts,
        ISet<(Guid, string)> mappedRevisionFacts,
        ICollection<PreparedDomainMutation> mutations)
    {
        ManualAcquisition mapped = MapAcquisition(
            ((ManualAcquisitionState)item.Value).Acquisition,
            targetAccountId);
        string entityId = mapped.AcquisitionId.ToString();
        EnsureArchiveDomainFactAbsent(
            connection,
            ManualAcquisitionEntityKind,
            entityId);
        mutations.Add(PreparedDomainMutation.Insert(
            targetArchiveId,
            CreatePortableAcquisitionRow(
                mapped,
                targetArchiveId,
                NextArchiveDomainFactVersion(
                    connection,
                    targetArchiveId,
                    targetAccountId,
                    targetRoleId,
                    ManualAcquisitionEntityKind,
                    entityId))));
        if (sourceRevisionFacts.Contains((
                item.Plan.SourceAccountReference,
                item.Plan.StableReference)))
        {
            mappedRevisionFacts.Add((targetAccountId, item.Plan.StableReference));
        }
    }

    private static ArchiveDomainFactRow CreateArchiveItemRow(
        SQLiteConnection connection,
        ItemStateObservation source,
        Guid targetAccountId,
        Guid archiveId,
        Guid? roleIdentityId)
    {
        var mapped = new ItemStateObservation(
            source.ObservationId,
            targetAccountId,
            source.Kind,
            source.CollectionCompleteness,
            source.Entries,
            source.Provenance);
        string payload = JsonSerializer.Serialize(
            ItemObservationPayload.FromDomain(mapped),
            DomainJsonOptions);
        string entityId = mapped.ObservationId.ToString();
        EnsureArchiveDomainFactAbsent(
            connection,
            ItemObservationEntityKind,
            entityId);
        return new ArchiveDomainFactRow
        {
            DomainKind = ItemObservationEntityKind,
            EntityId = entityId,
            GameAccountId = targetAccountId.ToString("D"),
            ArchiveId = archiveId.ToString("D"),
            TypeKey = ((int)mapped.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            SequenceUtcTicks = mapped.Provenance.Timestamps.ObservedAt?
                .UtcDateTime.Ticks,
            Version = NextArchiveDomainFactVersion(
                connection,
                archiveId,
                targetAccountId,
                roleIdentityId,
                ItemObservationEntityKind,
                entityId),
            ContentHash = ComputeDomainHash(payload),
            SnapshotFormatVersion = DomainSnapshotFormatVersion,
            SnapshotJson = payload,
        };
    }

    private static ArchiveDomainFactRow CreateArchiveChallengeRow(
        SQLiteConnection connection,
        ChallengeObservation source,
        Guid targetAccountId,
        Guid archiveId,
        Guid? roleIdentityId)
    {
        var mapped = new ChallengeObservation(
            source.ObservationId,
            targetAccountId,
            source.Mode,
            source.SeasonId,
            source.PeriodStart,
            source.PeriodEnd,
            source.Completeness,
            source.SpiralAbyssFloors,
            source.Provenance);
        string payload = JsonSerializer.Serialize(
            ChallengeObservationPayload.FromDomain(mapped),
            DomainJsonOptions);
        string entityId = mapped.ObservationId.ToString();
        EnsureArchiveDomainFactAbsent(
            connection,
            ChallengeObservationEntityKind,
            entityId);
        return new ArchiveDomainFactRow
        {
            DomainKind = ChallengeObservationEntityKind,
            EntityId = entityId,
            GameAccountId = targetAccountId.ToString("D"),
            ArchiveId = archiveId.ToString("D"),
            TypeKey = $"{(int)mapped.Mode}:{mapped.SeasonId}",
            SequenceUtcTicks = mapped.Provenance.Timestamps.ObservedAt?
                .UtcDateTime.Ticks,
            Version = NextArchiveDomainFactVersion(
                connection,
                archiveId,
                targetAccountId,
                roleIdentityId,
                ChallengeObservationEntityKind,
                entityId),
            ContentHash = ComputeDomainHash(payload),
            SnapshotFormatVersion = DomainSnapshotFormatVersion,
            SnapshotJson = payload,
        };
    }

    private static void EnsureArchiveDomainFactAbsent(
        SQLiteConnection connection,
        string domainKind,
        string entityId)
    {
        if (ReadDomainCurrent(connection, domainKind, entityId) is not null)
        {
            throw new ArchiveRestoreApplyConflictException(
                "A planned domain fact now exists in the target.");
        }
    }

    private static long NextArchiveDomainFactVersion(
        SQLiteConnection connection,
        Guid archiveId,
        Guid accountId,
        Guid? roleIdentityId,
        string domainKind,
        string entityId)
    {
        long entityVersion = connection.ExecuteScalar<long>(
            """
            SELECT COALESCE(MAX(COALESCE(AfterVersion, BeforeVersion, 0)), 0)
            FROM EntityChanges
            WHERE EntityKind = ? AND EntityReference = ?;
            """,
            domainKind,
            $"{accountId:D}/{entityId}");
        string tombstoneKey = CreateDomainTombstoneKey(
            archiveId,
            accountId,
            roleIdentityId?.ToString("D"),
            domainKind,
            entityId);
        long tombstoneVersion = connection.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(TombstoneVersion), 0) FROM LocalOnlyTombstones WHERE TombstoneKey = ?;",
            tombstoneKey);
        return checked(Math.Max(entityVersion, tombstoneVersion) + 1);
    }

    private static void ValidateArchiveSourceRevisions(
        SQLiteConnection connection,
        IEnumerable<MaterializedArchiveFact> revisions)
    {
        foreach (MaterializedArchiveFact revision in revisions)
        {
            string table = revision.Plan.Kind ==
                ArchiveRestoreFactKind.GachaRevision
                    ? "GachaRevisions"
                    : "ArchiveDomainRevisions";
            if (connection.ExecuteScalar<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE RevisionId = ?;",
                    revision.Plan.StableReference) != 0)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned source Revision ID now exists in the target.");
            }
        }
    }

    private static void ValidateArchiveTombstones(
        SQLiteConnection connection,
        IEnumerable<ArchiveRestoreTombstoneHit> tombstones)
    {
        foreach (ArchiveRestoreTombstoneHit tombstone in tombstones)
        {
            if (!tombstone.Confirmed || connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*) FROM LocalOnlyTombstones
                    WHERE TombstoneKey = ? AND TombstoneVersion = ?
                        AND IsActive = 1;
                    """,
                    tombstone.TombstoneKey,
                    tombstone.TombstoneVersion) != 1)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A confirmed Tombstone changed after preview.");
            }
        }
    }

    private static int ArchiveUtf8Bytes(string? value) =>
        value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static long EstimateArchiveRevisionBytes(object value) =>
        value switch
        {
            GachaRevisionItem revision =>
                ArchiveUtf8Bytes(JsonSerializer.Serialize(revision.Before)) +
                ArchiveUtf8Bytes(JsonSerializer.Serialize(revision.After)),
            ManualAcquisitionRevisionItem revision =>
                ArchiveUtf8Bytes(JsonSerializer.Serialize(revision.Before)) +
                ArchiveUtf8Bytes(JsonSerializer.Serialize(revision.After)),
            _ => 0,
        };
}
