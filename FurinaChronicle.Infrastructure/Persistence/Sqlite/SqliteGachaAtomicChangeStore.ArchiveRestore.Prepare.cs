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
        var revisionRemovals = request.Plan.Removals
            .Where(item => item.Kind is
                ArchiveRestoreFactKind.GachaRevision or
                ArchiveRestoreFactKind.ManualAcquisitionRevision)
            .Select(item => new PreparedArchiveRevisionRemoval(
                item.Kind,
                item.TargetAccountId,
                item.StableReference))
            .ToList();
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
                    items.Add(PrepareArchiveDomainFact(
                        connection,
                        item,
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
                    challenges.Add(PrepareArchiveDomainFact(
                        connection,
                        item,
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
                    if (item.Plan.Action == ArchiveRestoreFactAction.Replace)
                    {
                        revisionRemovals.Add(
                            new PreparedArchiveRevisionRemoval(
                                item.Plan.Kind,
                                targetAccountId,
                                item.Plan.StableReference));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        foreach (ArchiveRestoreTargetFactRemoval removal in
                 request.Plan.Removals.Where(item => item.Kind is not
                     ArchiveRestoreFactKind.GachaRevision and not
                     ArchiveRestoreFactKind.ManualAcquisitionRevision))
        {
            PrepareArchiveTargetRemoval(
                connection,
                removal,
                gacha,
                items,
                manuals,
                challenges);
        }
        ValidateArchiveRevisionRemovals(connection, revisionRemovals);
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
            challenges.Count + revisions.Count + revisionRemovals.Count;
        int infrastructureCount = parents.ArchivesToInsert.Count +
            parents.AccountsToInsert.Count +
            parents.IdentitiesToInsert.Count +
            parents.AliasesToInsert.Count + tombstones.Length +
            request.Plan.AccountRemovals.Count +
            (settingsChange ? 1 : 0);
        long snapshotBytes = gacha.Sum(item =>
                ArchiveUtf8Bytes(SerializeSnapshot(item.Before)) +
                ArchiveUtf8Bytes(SerializeSnapshot(item.After))) +
            items.Concat(manuals).Concat(challenges)
                .Sum(item => ArchiveUtf8Bytes(item.BeforeSnapshotJson) +
                    ArchiveUtf8Bytes(item.AfterSnapshotJson)) +
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
            revisionRemovals.Distinct().ToArray(),
            request.Plan.AccountRemovals,
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
        GachaRecordRow? existing = ReadCurrent(connection, reference);
        if (item.Plan.Action == ArchiveRestoreFactAction.Replace)
        {
            if (existing is null ||
                !string.Equals(
                    ArchiveRestoreSemanticDigest.ForGacha(existing.ToDomain()),
                    item.Plan.TargetSemanticDigest,
                    StringComparison.Ordinal))
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned Gacha replacement target changed.");
            }
        }
        else if (existing is not null)
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
        if (existing is not null)
        {
            row.Id = existing.Id;
            row.Version = Math.Max(
                row.Version,
                checked(existing.Version + 1));
        }
        mutations.Add(new PreparedMutation(
            targetArchiveId,
            existing,
            row,
            GachaRecordChangeKind.Substantive,
            existing is null
                ? EntityChangeKind.Insert
                : EntityChangeKind.Update));
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
        ArchiveDomainFactRow after = CreatePortableAcquisitionRow(
            mapped,
            targetArchiveId,
            NextArchiveDomainFactVersion(
                connection,
                targetArchiveId,
                targetAccountId,
                targetRoleId,
                ManualAcquisitionEntityKind,
                entityId));
        mutations.Add(PrepareArchiveDomainFact(
            connection,
            item,
            targetArchiveId,
            after));
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

    private static PreparedDomainMutation PrepareArchiveDomainFact(
        SQLiteConnection connection,
        MaterializedArchiveFact item,
        Guid archiveId,
        ArchiveDomainFactRow after)
    {
        ArchiveDomainFactRow? existing = ReadDomainCurrent(
            connection,
            after.DomainKind,
            after.EntityId);
        if (item.Plan.Action == ArchiveRestoreFactAction.Replace)
        {
            if (existing is null ||
                !string.Equals(
                    ComputeArchiveDomainDigest(item.Plan.Kind, existing),
                    item.Plan.TargetSemanticDigest,
                    StringComparison.Ordinal))
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned domain replacement target changed.");
            }
            return PreparedDomainMutation.Update(archiveId, existing, after);
        }
        if (existing is not null)
        {
            throw new ArchiveRestoreApplyConflictException(
                "A planned domain fact now exists in the target.");
        }
        return PreparedDomainMutation.Insert(archiveId, after);
    }

    private static string ComputeArchiveDomainDigest(
        ArchiveRestoreFactKind kind,
        ArchiveDomainFactRow row) => kind switch
        {
            ArchiveRestoreFactKind.ItemObservation =>
                ArchiveRestoreSemanticDigest.ForItemObservation(
                    DeserializeItemObservation(row.SnapshotJson)),
            ArchiveRestoreFactKind.ManualAcquisition =>
                ArchiveRestoreSemanticDigest.ForManualAcquisition(
                    new ManualAcquisitionState(
                        DeserializeManualAcquisition(row.SnapshotJson),
                        new FactVersion(row.Version),
                        ParseRequiredGuid(row.ArchiveId, "archive"))),
            ArchiveRestoreFactKind.ChallengeObservation =>
                ArchiveRestoreSemanticDigest.ForChallenge(
                    DeserializeChallengeObservation(row.SnapshotJson)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void PrepareArchiveTargetRemoval(
        SQLiteConnection connection,
        ArchiveRestoreTargetFactRemoval removal,
        ICollection<PreparedMutation> gacha,
        ICollection<PreparedDomainMutation> items,
        ICollection<PreparedDomainMutation> manuals,
        ICollection<PreparedDomainMutation> challenges)
    {
        if (removal.Kind == ArchiveRestoreFactKind.GachaCurrent)
        {
            var reference = new GachaFactReference(
                removal.TargetAccountId,
                removal.StableReference);
            GachaRecordRow? existing = ReadCurrent(connection, reference);
            if (existing is null ||
                !string.Equals(
                    ArchiveRestoreSemanticDigest.ForGacha(existing.ToDomain()),
                    removal.TargetSemanticDigest,
                    StringComparison.Ordinal))
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned Gacha removal target changed.");
            }
            gacha.Add(new PreparedMutation(
                removal.TargetArchiveId,
                existing,
                After: null,
                GachaRecordChangeKind.Substantive,
                EntityChangeKind.Delete));
            return;
        }
        string domainKind = removal.Kind switch
        {
            ArchiveRestoreFactKind.ItemObservation =>
                ItemObservationEntityKind,
            ArchiveRestoreFactKind.ManualAcquisition =>
                ManualAcquisitionEntityKind,
            ArchiveRestoreFactKind.ChallengeObservation =>
                ChallengeObservationEntityKind,
            _ => throw new ArgumentOutOfRangeException(nameof(removal)),
        };
        ArchiveDomainFactRow? row = ReadDomainCurrent(
            connection,
            domainKind,
            removal.StableReference);
        if (row is null ||
            ParseRequiredGuid(row.GameAccountId, "game account") !=
                removal.TargetAccountId ||
            !string.Equals(
                ComputeArchiveDomainDigest(removal.Kind, row),
                removal.TargetSemanticDigest,
                StringComparison.Ordinal))
        {
            throw new ArchiveRestoreApplyConflictException(
                "A planned domain removal target changed.");
        }
        PreparedDomainMutation mutation = PreparedDomainMutation.Delete(
            removal.TargetArchiveId,
            row);
        ICollection<PreparedDomainMutation> destination = removal.Kind switch
        {
            ArchiveRestoreFactKind.ItemObservation => items,
            ArchiveRestoreFactKind.ManualAcquisition => manuals,
            ArchiveRestoreFactKind.ChallengeObservation => challenges,
            _ => throw new ArgumentOutOfRangeException(nameof(removal)),
        };
        destination.Add(mutation);
    }

    private static void ValidateArchiveRevisionRemovals(
        SQLiteConnection connection,
        IEnumerable<PreparedArchiveRevisionRemoval> removals)
    {
        foreach (PreparedArchiveRevisionRemoval removal in removals.Distinct())
        {
            string table = removal.Kind ==
                ArchiveRestoreFactKind.GachaRevision
                    ? "GachaRevisions"
                    : "ArchiveDomainRevisions";
            if (connection.ExecuteScalar<int>(
                    $"SELECT COUNT(*) FROM {table} " +
                    "WHERE RevisionId = ? AND GameAccountId = ?;",
                    removal.StableReference,
                    removal.TargetAccountId.ToString("D")) != 1)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned source Revision removal target changed.");
            }
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
            int existing = connection.ExecuteScalar<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE RevisionId = ?;",
                    revision.Plan.StableReference);
            if (existing != 0 &&
                revision.Plan.Action != ArchiveRestoreFactAction.Replace)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned source Revision ID now exists in the target.");
            }
            if (revision.Plan.Action == ArchiveRestoreFactAction.Replace &&
                existing != 1)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A planned source Revision replacement target changed.");
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
