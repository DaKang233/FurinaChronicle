// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.Gacha.History;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    internal const string ItemObservationEntityKind = "item_observation";
    internal const string ManualAcquisitionEntityKind = "manual_acquisition";
    internal const string ChallengeObservationEntityKind =
        "challenge_observation";
    private const int DomainSnapshotFormatVersion = 1;

    private static readonly JsonSerializerOptions DomainJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<ArchiveOperationResult> CommitItemObservationsAsync(
        ItemObservationCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DomainProposal[] proposals = request.Observations
            .Select(observation =>
            {
                ItemObservationPayload payload =
                    ItemObservationPayload.FromDomain(observation);
                return DomainProposal.Immutable(
                    ItemObservationEntityKind,
                    observation.ObservationId.ToString(),
                    observation.GameAccountId,
                    ((int)observation.Kind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    observation.Provenance.Timestamps.ObservedAt?
                        .UtcDateTime.Ticks,
                    JsonSerializer.Serialize(payload, DomainJsonOptions));
            })
            .ToArray();
        return CommitDomainAsync(
            DomainCommitEnvelope.From(request),
            proposals,
            cancellationToken);
    }

    public Task<ArchiveOperationResult> CommitManualAcquisitionsAsync(
        ManualAcquisitionCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DomainProposal[] proposals = request.Mutations
            .Select(mutation => mutation.Proposed is null
                ? DomainProposal.Delete(
                    ManualAcquisitionEntityKind,
                    mutation.AcquisitionId.ToString(),
                    mutation.GameAccountId,
                    mutation.ExpectedVersion)
                : DomainProposal.Mutable(
                    ManualAcquisitionEntityKind,
                    mutation.AcquisitionId.ToString(),
                    mutation.GameAccountId,
                    mutation.Proposed.TypeId,
                    mutation.Proposed.OccurredAt.UtcStart?.UtcDateTime.Ticks,
                    JsonSerializer.Serialize(
                        ManualAcquisitionPayload.FromDomain(mutation.Proposed),
                        DomainJsonOptions),
                    mutation.ExpectedVersion))
            .ToArray();
        return CommitDomainAsync(
            DomainCommitEnvelope.From(request),
            proposals,
            cancellationToken);
    }

    public Task<ArchiveOperationResult> CommitChallengeObservationsAsync(
        ChallengeObservationCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Observations.Any(observation =>
                observation.Mode != ChallengeMode.SpiralAbyss))
        {
            return Task.FromResult(new ArchiveOperationResult(
                ChangeExecutionStatus.Conflict,
                ChangeSetId: null,
                AffectedRecordCount: 0,
                "Only Spiral Abyss challenge observations are supported in Phase 8B.5."));
        }

        DomainProposal[] proposals = request.Observations
            .Select(observation => DomainProposal.Immutable(
                ChallengeObservationEntityKind,
                observation.ObservationId.ToString(),
                observation.GameAccountId,
                $"{(int)observation.Mode}:{observation.SeasonId}",
                observation.Provenance.Timestamps.ObservedAt?
                    .UtcDateTime.Ticks,
                JsonSerializer.Serialize(
                    ChallengeObservationPayload.FromDomain(observation),
                    DomainJsonOptions)))
            .ToArray();
        return CommitDomainAsync(
            DomainCommitEnvelope.From(request),
            proposals,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ItemStateObservation>>
        GetItemObservationsAsync(
            Guid gameAccountId,
            InventoryItemKind? kind = null,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(gameAccountId);
        await database.InitializeAsync(cancellationToken);
        string sql =
            "SELECT * FROM ArchiveDomainFacts " +
            "WHERE GameAccountId = ? AND DomainKind = ?" +
            (kind is null ? string.Empty : " AND TypeKey = ?") +
            " ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks, EntityId;";
        object[] arguments = kind is null
            ? [gameAccountId.ToString("D"), ItemObservationEntityKind]
            : [
                gameAccountId.ToString("D"),
                ItemObservationEntityKind,
                ((int)kind.Value).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ];
        List<ArchiveDomainFactRow> rows =
            await database.Connection.QueryAsync<ArchiveDomainFactRow>(
                sql,
                arguments);
        return rows.Select(row => DeserializeItemObservation(row.SnapshotJson))
            .ToArray();
    }

    public async Task<IReadOnlyList<ManualAcquisitionState>>
        GetManualAcquisitionsAsync(
            Guid gameAccountId,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(gameAccountId);
        await database.InitializeAsync(cancellationToken);
        List<ArchiveDomainFactRow> rows =
            await database.Connection.QueryAsync<ArchiveDomainFactRow>(
                """
                SELECT * FROM ArchiveDomainFacts
                WHERE GameAccountId = ? AND DomainKind = ?
                ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks, EntityId;
                """,
                gameAccountId.ToString("D"),
                ManualAcquisitionEntityKind);
        return rows.Select(row => new ManualAcquisitionState(
                DeserializeManualAcquisition(row.SnapshotJson),
                new FactVersion(row.Version),
                ParseRequiredGuid(row.ArchiveId, "archive")))
            .ToArray();
    }

    public async Task<IReadOnlyList<ChallengeObservation>>
        GetChallengeObservationsAsync(
            Guid gameAccountId,
            ChallengeMode? mode = null,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(gameAccountId);
        await database.InitializeAsync(cancellationToken);
        List<ArchiveDomainFactRow> rows =
            await database.Connection.QueryAsync<ArchiveDomainFactRow>(
                """
                SELECT * FROM ArchiveDomainFacts
                WHERE GameAccountId = ? AND DomainKind = ?
                ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks, EntityId;
                """,
                gameAccountId.ToString("D"),
                ChallengeObservationEntityKind);
        return rows.Select(row =>
                DeserializeChallengeObservation(row.SnapshotJson))
            .Where(observation => mode is null || observation.Mode == mode)
            .ToArray();
    }

    public async Task<IReadOnlyList<DomainRevisionItem>>
        GetDomainRevisionsAsync(
            Guid changeSetId,
            CancellationToken cancellationToken = default)
    {
        if (changeSetId == Guid.Empty)
        {
            throw new ArgumentException(
                "Change set ID cannot be empty.",
                nameof(changeSetId));
        }
        await database.InitializeAsync(cancellationToken);
        List<ArchiveDomainRevisionRow> rows = await database.Connection
            .QueryAsync<ArchiveDomainRevisionRow>(
                """
                SELECT * FROM ArchiveDomainRevisions
                WHERE ChangeSetId = ? ORDER BY CreatedAtUtcTicks, rowid;
                """,
                changeSetId.ToString("D"));
        return rows.Select(row => new DomainRevisionItem(
                ParseRequiredGuid(row.RevisionId, "revision"),
                ParseRequiredGuid(row.ChangeSetId, "change set"),
                row.DomainKind,
                $"{row.GameAccountId}/{row.EntityId}",
                (EntityChangeKind)row.ChangeKind,
                row.BeforeVersion is null
                    ? null
                    : new FactVersion(row.BeforeVersion.Value),
                row.AfterVersion is null
                    ? null
                    : new FactVersion(row.AfterVersion.Value),
                row.BeforeSnapshotJson,
                row.AfterSnapshotJson,
                new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero)))
            .ToArray();
    }
    private async Task<ArchiveOperationResult> CommitDomainAsync(
        DomainCommitEnvelope envelope,
        IReadOnlyCollection<DomainProposal> proposals,
        CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        ArchiveOperationResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitResultRow? committed = ReadCommitResult(
                connection,
                envelope.OperationId);
            if (committed is not null)
            {
                result = new ArchiveOperationResult(
                    ChangeExecutionStatus.AlreadyCommitted,
                    ParseOptionalGuid(committed.ChangeSetId),
                    committed.AffectedRecordCount,
                    OriginalStatus: (ChangeExecutionStatus)committed.Status);
                return;
            }

            var prepared = new List<PreparedDomainMutation>();
            int suppressed = 0;
            foreach (DomainProposal proposal in proposals)
            {
                Guid archiveId = ReadArchiveId(
                    connection,
                    proposal.GameAccountId);
                if (ReadActiveDomainTombstone(
                        connection,
                        archiveId,
                        proposal) is not null)
                {
                    suppressed++;
                    continue;
                }

                ArchiveDomainFactRow? current = ReadDomainCurrent(
                    connection,
                    proposal.DomainKind,
                    proposal.EntityId);
                if (current is not null &&
                    ParseRequiredGuid(current.GameAccountId, "game account") !=
                    proposal.GameAccountId)
                {
                    result = DomainConflict(
                        "A stable domain entity ID is already owned by another account.");
                    return;
                }

                if (proposal.IsDelete)
                {
                    if (current is null)
                    {
                        continue;
                    }
                    if (proposal.ExpectedVersion is null ||
                        current.Version != proposal.ExpectedVersion.Value.Value)
                    {
                        result = DomainConflict(
                            "The domain fact changed before deletion.");
                        return;
                    }
                    prepared.Add(PreparedDomainMutation.Delete(
                        archiveId,
                        current));
                    continue;
                }

                string payload = proposal.PayloadJson ??
                    throw new InvalidDataException(
                        "A non-delete domain proposal has no payload.");
                string hash = ComputeDomainHash(payload);
                if (proposal.DomainKind == ChallengeObservationEntityKind)
                {
                    ChallengeObservationPayload challenge =
                        DeserializeChallengePayload(payload);
                    if (proposal.SequenceUtcTicks is null &&
                        string.IsNullOrWhiteSpace(
                            challenge.Provenance.SourceRecordId))
                    {
                        result = DomainConflict(
                            "Ambiguous challenge observation: neither a trusted observation time nor a stable source record ID is available.");
                        return;
                    }
                    if (proposal.SequenceUtcTicks is long sequence)
                    {
                        ArchiveDomainFactRow[] sameTime = connection
                            .Query<ArchiveDomainFactRow>(
                                """
                                SELECT * FROM ArchiveDomainFacts
                                WHERE GameAccountId = ? AND DomainKind = ?
                                  AND TypeKey = ? AND SequenceUtcTicks = ?
                                  AND EntityId <> ?;
                                """,
                                proposal.GameAccountId.ToString("D"),
                                proposal.DomainKind,
                                proposal.TypeKey,
                                sequence,
                                proposal.EntityId)
                            .ToArray();
                        foreach (ArchiveDomainFactRow candidate in sameTime)
                        {
                            ChallengeObservationPayload existing =
                                DeserializeChallengePayload(
                                    candidate.SnapshotJson);
                            if (ChallengeBusinessContentEquals(
                                    existing,
                                    challenge))
                            {
                                payload = string.Empty;
                                break;
                            }
                            result = DomainConflict(
                                "Conflicting challenge observations share the same trusted observation time.");
                            return;
                        }
                        if (payload.Length == 0)
                        {
                            continue;
                        }
                    }
                }
                if (current is null)
                {
                    if (proposal.ExpectedVersion is not null)
                    {
                        result = DomainConflict(
                            "The expected domain fact no longer exists.");
                        return;
                    }
                    prepared.Add(PreparedDomainMutation.Insert(
                        archiveId,
                        proposal.ToRow(archiveId, 1, hash)));
                    continue;
                }
                if (string.Equals(
                        current.ContentHash,
                        hash,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (proposal.IsImmutable)
                {
                    result = DomainConflict(
                        "The stable observation ID already exists with different content.");
                    return;
                }
                if (proposal.ExpectedVersion is null ||
                    current.Version != proposal.ExpectedVersion.Value.Value)
                {
                    result = DomainConflict(
                        "The domain fact changed before correction.");
                    return;
                }
                prepared.Add(PreparedDomainMutation.Update(
                    archiveId,
                    current,
                    proposal.ToRow(
                        archiveId,
                        checked(current.Version + 1),
                        hash)));
            }

            if (result is not null)
            {
                return;
            }
            if (prepared.Count == 0)
            {
                ChangeExecutionStatus status = suppressed > 0
                    ? ChangeExecutionStatus.Suppressed
                    : ChangeExecutionStatus.NoOp;
                InsertDomainCommitResult(
                    connection,
                    envelope,
                    changeSetId: null,
                    status,
                    affectedRecordCount: 0);
                result = new ArchiveOperationResult(
                    status,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    suppressed > 0
                        ? $"Suppressed {suppressed} permanently deleted facts."
                        : null);
                return;
            }

            Guid[] archiveIds = prepared.Select(item => item.ArchiveId)
                .Distinct()
                .ToArray();
            long snapshotBytes = prepared.Sum(item =>
                Encoding.UTF8.GetByteCount(
                    item.BeforeSnapshotJson ?? string.Empty) +
                Encoding.UTF8.GetByteCount(
                    item.AfterSnapshotJson ?? string.Empty));
            HistoryCleanupPlan? cleanupPlan = BuildCleanupPlan(
                connection,
                envelope.OperationId,
                envelope.CaptureUndo,
                archiveIds,
                prepared.Count,
                snapshotBytes);
            if (cleanupPlan is not null)
            {
                if (cleanupPlan.Candidates.Count == 0 ||
                    cleanupPlan.AvailableBytes + cleanupPlan.ReclaimableBytes <
                    cleanupPlan.RequiredBytes)
                {
                    throw new IOException(
                        "There is not enough storage for the archive domain change, even after eligible undo material cleanup.");
                }
                if (envelope.CleanupConfirmationId !=
                    cleanupPlan.ConfirmationId)
                {
                    result = new ArchiveOperationResult(
                        ChangeExecutionStatus.NeedsConfirmation,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        CleanupPlan: cleanupPlan);
                    return;
                }
                EvictChangeSets(
                    connection,
                    cleanupPlan.Candidates.Select(item => item.ChangeSetId));
            }

            Guid changeSetId = Guid.NewGuid();
            InsertDomainChangeSet(
                connection,
                envelope,
                changeSetId,
                archiveIds,
                prepared.Count);
            bool captureUndo = envelope.CaptureUndo &&
                UndoRetentionPolicy.ShouldCaptureNewMaterial(
                    archiveIds.Select(id => ReadUndoLimit(connection, id)));
            long materialBytes = 0;
            foreach (PreparedDomainMutation mutation in prepared)
            {
                ApplyDomainMutation(connection, mutation);
                InsertDomainEntityChangeAndRevision(
                    connection,
                    changeSetId,
                    envelope.CommittedAt,
                    mutation);
                if (captureUndo)
                {
                    materialBytes += InsertDomainUndoMaterial(
                        connection,
                        changeSetId,
                        mutation);
                }
            }
            connection.Execute(
                """
                INSERT INTO OperationHistory
                    (ChangeSetId, IsUndoEligible, IneligibilityReason,
                     UndoneByChangeSetId, MaterialBytes)
                VALUES (?, ?, ?, NULL, ?);
                """,
                changeSetId.ToString("D"),
                captureUndo ? 1 : 0,
                (int)(captureUndo
                    ? UndoIneligibilityReason.None
                    : UndoIneligibilityReason.Disabled),
                materialBytes);
            InsertDomainCommitResult(
                connection,
                envelope,
                changeSetId,
                ChangeExecutionStatus.Applied,
                prepared.Count);
            ApplyCapacityLimits(connection, archiveIds);
            result = new ArchiveOperationResult(
                ChangeExecutionStatus.Applied,
                changeSetId,
                prepared.Count,
                suppressed > 0
                    ? $"Suppressed {suppressed} permanently deleted facts."
                    : null);
        });

        return result ?? throw new InvalidOperationException(
            "The archive domain transaction did not produce a result.");
    }
    private static void InsertDomainChangeSet(
        SQLiteConnection connection,
        DomainCommitEnvelope envelope,
        Guid changeSetId,
        IReadOnlyCollection<Guid> archiveIds,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO DataChangeSets
                (ChangeSetId, OperationId, OperationKind,
                 StartedAtUtcTicks, StartedAtOffsetMinutes,
                 CommittedAtUtcTicks, CommittedAtOffsetMinutes,
                 Origin, Summary, AffectedRecordCount, UndoOfChangeSetId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL);
            """,
            changeSetId.ToString("D"),
            envelope.OperationId.ToString(),
            (int)envelope.OperationKind,
            envelope.StartedAt.UtcDateTime.Ticks,
            checked((int)envelope.StartedAt.Offset.TotalMinutes),
            envelope.CommittedAt.UtcDateTime.Ticks,
            checked((int)envelope.CommittedAt.Offset.TotalMinutes),
            (int)envelope.Origin,
            envelope.Summary,
            affectedRecordCount);
        foreach (Guid archiveId in archiveIds)
        {
            connection.Execute(
                "INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId) VALUES (?, ?);",
                changeSetId.ToString("D"),
                archiveId.ToString("D"));
        }
    }

    private static void ApplyDomainMutation(
        SQLiteConnection connection,
        PreparedDomainMutation mutation)
    {
        if (mutation.After is null)
        {
            connection.Execute(
                "DELETE FROM ArchiveDomainFacts WHERE DomainKind = ? AND EntityId = ?;",
                mutation.Before!.DomainKind,
                mutation.Before.EntityId);
            return;
        }
        if (mutation.Before is null)
        {
            InsertDomainFact(connection, mutation.After);
            return;
        }
        connection.Execute(
            """
            UPDATE ArchiveDomainFacts
            SET GameAccountId = ?, ArchiveId = ?, TypeKey = ?,
                SequenceUtcTicks = ?, Version = ?, ContentHash = ?,
                SnapshotFormatVersion = ?, SnapshotJson = ?
            WHERE DomainKind = ? AND EntityId = ?;
            """,
            mutation.After.GameAccountId,
            mutation.After.ArchiveId,
            mutation.After.TypeKey,
            mutation.After.SequenceUtcTicks,
            mutation.After.Version,
            mutation.After.ContentHash,
            mutation.After.SnapshotFormatVersion,
            mutation.After.SnapshotJson,
            mutation.After.DomainKind,
            mutation.After.EntityId);
    }

    private static void InsertDomainFact(
        SQLiteConnection connection,
        ArchiveDomainFactRow row) =>
        connection.Execute(
            """
            INSERT INTO ArchiveDomainFacts
                (DomainKind, EntityId, GameAccountId, ArchiveId, TypeKey,
                 SequenceUtcTicks, Version, ContentHash,
                 SnapshotFormatVersion, SnapshotJson)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            row.DomainKind,
            row.EntityId,
            row.GameAccountId,
            row.ArchiveId,
            row.TypeKey,
            row.SequenceUtcTicks,
            row.Version,
            row.ContentHash,
            row.SnapshotFormatVersion,
            row.SnapshotJson);

    private static void InsertDomainEntityChangeAndRevision(
        SQLiteConnection connection,
        Guid changeSetId,
        DateTimeOffset committedAt,
        PreparedDomainMutation mutation)
    {
        ArchiveDomainFactRow identity = mutation.After ?? mutation.Before!;
        string reference = $"{identity.GameAccountId}/{identity.EntityId}";
        connection.Execute(
            """
            INSERT INTO EntityChanges
                (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                 BeforeVersion, AfterVersion)
            VALUES (?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            identity.DomainKind,
            reference,
            (int)mutation.ChangeKind,
            mutation.Before?.Version,
            mutation.After?.Version);
        connection.Execute(
            """
            INSERT INTO ArchiveDomainRevisions
                (RevisionId, ChangeSetId, DomainKind, EntityId,
                 GameAccountId, ChangeKind, BeforeVersion, AfterVersion,
                 SnapshotFormatVersion, BeforeSnapshotJson,
                 AfterSnapshotJson, CreatedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            Guid.NewGuid().ToString("D"),
            changeSetId.ToString("D"),
            identity.DomainKind,
            identity.EntityId,
            identity.GameAccountId,
            (int)mutation.ChangeKind,
            mutation.Before?.Version,
            mutation.After?.Version,
            DomainSnapshotFormatVersion,
            mutation.BeforeSnapshotJson,
            mutation.AfterSnapshotJson,
            committedAt.UtcDateTime.Ticks);
    }

    private static long InsertDomainUndoMaterial(
        SQLiteConnection connection,
        Guid changeSetId,
        PreparedDomainMutation mutation)
    {
        ArchiveDomainFactRow identity = mutation.After ?? mutation.Before!;
        long bytes =
            Encoding.UTF8.GetByteCount(
                mutation.BeforeSnapshotJson ?? string.Empty) +
            Encoding.UTF8.GetByteCount(
                mutation.AfterSnapshotJson ?? string.Empty);
        connection.Execute(
            """
            INSERT INTO UndoMaterials
                (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                 ExpectedAfterVersion, SnapshotFormatVersion,
                 BeforeSnapshotJson, AfterSnapshotJson, MaterialBytes)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            identity.DomainKind,
            $"{identity.GameAccountId}/{identity.EntityId}",
            (int)mutation.ChangeKind,
            mutation.After?.Version,
            DomainSnapshotFormatVersion,
            mutation.BeforeSnapshotJson,
            mutation.AfterSnapshotJson,
            bytes);
        return bytes;
    }

    private static void InsertDomainCommitResult(
        SQLiteConnection connection,
        DomainCommitEnvelope envelope,
        Guid? changeSetId,
        ChangeExecutionStatus status,
        int affectedRecordCount) =>
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?);
            """,
            envelope.OperationId.ToString(),
            changeSetId?.ToString("D"),
            (int)status,
            affectedRecordCount,
            envelope.CommittedAt.UtcDateTime.Ticks);

    private static ArchiveDomainFactRow? ReadDomainCurrent(
        SQLiteConnection connection,
        string domainKind,
        string entityId) =>
        connection.Query<ArchiveDomainFactRow>(
                "SELECT * FROM ArchiveDomainFacts WHERE DomainKind = ? AND EntityId = ?;",
                domainKind,
                entityId)
            .SingleOrDefault();

    private static TombstoneRow? ReadActiveDomainTombstone(
        SQLiteConnection connection,
        Guid archiveId,
        DomainProposal proposal) =>
        connection.Query<TombstoneRow>(
                """
                SELECT TombstoneKey, TombstoneVersion, ArchiveId, IsActive,
                       ReintroducedByChangeSetId
                FROM LocalOnlyTombstones
                WHERE TombstoneKey = ? AND IsActive = 1;
                """,
                CreateDomainTombstoneKey(
                    archiveId,
                    proposal.GameAccountId,
                    ReadRoleIdentityId(
                        connection,
                        proposal.GameAccountId),
                    proposal.DomainKind,
                    proposal.EntityId))
            .SingleOrDefault();

    private static string CreateDomainTombstoneKey(
        Guid archiveId,
        Guid accountId,
        string? roleIdentityId,
        string domainKind,
        string entityId) =>
        string.IsNullOrWhiteSpace(roleIdentityId)
            ? $"domain-account|{archiveId:D}|{accountId:D}|{domainKind}|{entityId}"
            : $"domain-role|{archiveId:D}|{roleIdentityId}|{domainKind}|{entityId}";

    private static string? ReadRoleIdentityId(
        SQLiteConnection connection,
        Guid accountId) =>
        connection.ExecuteScalar<string?>(
            "SELECT GameRoleIdentityId FROM GameAccounts WHERE Id = ?;",
            accountId.ToString("D"));

    private static bool IsArchiveDomainEntityKind(string entityKind) =>
        entityKind is ItemObservationEntityKind or
            ManualAcquisitionEntityKind or ChallengeObservationEntityKind;

    private static GachaAtomicChangeResult UndoArchiveDomainChangeSet(
        SQLiteConnection connection,
        GachaUndoRequest request,
        Guid originalChangeSetId,
        IReadOnlyCollection<Guid> archiveIds,
        IReadOnlyCollection<UndoMaterialRow> materials)
    {
        var inverse = new List<PreparedDomainMutation>(materials.Count);
        foreach (UndoMaterialRow material in materials)
        {
            ArchiveDomainFactRow? originalBefore =
                DeserializeDomainSnapshot(material.BeforeSnapshotJson);
            ArchiveDomainFactRow? originalAfter =
                DeserializeDomainSnapshot(material.AfterSnapshotJson);
            ArchiveDomainFactRow identity = originalAfter ?? originalBefore ??
                throw new InvalidDataException(
                    "Undo material has no archive domain snapshot.");
            ArchiveDomainFactRow? current = ReadDomainCurrent(
                connection,
                identity.DomainKind,
                identity.EntityId);
            EntityChangeKind originalKind =
                (EntityChangeKind)material.ChangeKind;
            if (originalKind is EntityChangeKind.Insert or
                EntityChangeKind.Update)
            {
                if (current is null ||
                    material.ExpectedAfterVersion is null ||
                    current.Version != material.ExpectedAfterVersion.Value ||
                    originalAfter is null ||
                    !DomainRowsEqual(current, originalAfter))
                {
                    return new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        $"Archive domain fact {identity.DomainKind}/{identity.EntityId} changed after the original operation.");
                }
            }
            else if (originalKind == EntityChangeKind.Delete &&
                     current is not null)
            {
                return new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    $"Deleted archive domain fact {identity.DomainKind}/{identity.EntityId} was reintroduced.");
            }

            Guid archiveId = ParseRequiredGuid(identity.ArchiveId, "archive");
            if (!archiveIds.Contains(archiveId))
            {
                throw new InvalidDataException(
                    "Undo material references an unrelated archive.");
            }
            string tombstoneKey = CreateDomainTombstoneKey(
                archiveId,
                ParseRequiredGuid(identity.GameAccountId, "game account"),
                ReadRoleIdentityId(
                    connection,
                    ParseRequiredGuid(
                        identity.GameAccountId,
                        "game account")),
                identity.DomainKind,
                identity.EntityId);
            int tombstoneCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM LocalOnlyTombstones WHERE TombstoneKey = ? AND IsActive = 1;",
                tombstoneKey);
            if (tombstoneCount > 0)
            {
                return new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    $"Archive domain fact {identity.DomainKind}/{identity.EntityId} was irreversibly deleted.");
            }

            switch (originalKind)
            {
                case EntityChangeKind.Insert:
                    inverse.Add(PreparedDomainMutation.Delete(
                        archiveId,
                        current!));
                    break;
                case EntityChangeKind.Update:
                    ArchiveDomainFactRow restored = CloneDomainRow(
                        originalBefore ?? throw new InvalidDataException(
                            "Update undo material has no before snapshot."));
                    restored.Version = checked(current!.Version + 1);
                    inverse.Add(PreparedDomainMutation.Update(
                        archiveId,
                        current,
                        restored));
                    break;
                case EntityChangeKind.Delete:
                    ArchiveDomainFactRow reinserted = CloneDomainRow(
                        originalBefore ?? throw new InvalidDataException(
                            "Delete undo material has no before snapshot."));
                    reinserted.Version = checked(reinserted.Version + 1);
                    inverse.Add(PreparedDomainMutation.Insert(
                        archiveId,
                        reinserted));
                    break;
                default:
                    throw new InvalidDataException(
                        "Undo material has an invalid domain change kind.");
            }
        }

        foreach (PreparedDomainMutation mutation in inverse)
        {
            ApplyDomainMutation(connection, mutation);
        }
        Guid undoChangeSetId = Guid.NewGuid();
        InsertUndoChangeSet(
            connection,
            request,
            undoChangeSetId,
            originalChangeSetId,
            archiveIds,
            inverse.Count);
        foreach (PreparedDomainMutation mutation in inverse)
        {
            InsertDomainEntityChangeAndRevision(
                connection,
                undoChangeSetId,
                request.CommittedAt,
                mutation);
        }
        connection.Execute(
            """
            INSERT INTO OperationHistory
                (ChangeSetId, IsUndoEligible, IneligibilityReason,
                 UndoneByChangeSetId, MaterialBytes)
            VALUES (?, 0, ?, NULL, 0);
            """,
            undoChangeSetId.ToString("D"),
            (int)UndoIneligibilityReason.InverseOperation);
        connection.Execute(
            """
            UPDATE OperationHistory
            SET IsUndoEligible = 0, IneligibilityReason = ?,
                UndoneByChangeSetId = ?, MaterialBytes = 0
            WHERE ChangeSetId = ? AND IsUndoEligible = 1;
            """,
            (int)UndoIneligibilityReason.AlreadyUndone,
            undoChangeSetId.ToString("D"),
            originalChangeSetId.ToString("D"));
        connection.Execute(
            "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
            originalChangeSetId.ToString("D"));
        InsertUndoCommitResult(
            connection,
            request,
            undoChangeSetId,
            inverse.Count);
        return new GachaAtomicChangeResult(
            ChangeExecutionStatus.Applied,
            undoChangeSetId,
            inverse.Count);
    }

    private static bool DomainRowsEqual(
        ArchiveDomainFactRow left,
        ArchiveDomainFactRow right) =>
        left.DomainKind == right.DomainKind &&
        left.EntityId == right.EntityId &&
        left.GameAccountId == right.GameAccountId &&
        left.ArchiveId == right.ArchiveId &&
        left.TypeKey == right.TypeKey &&
        left.SequenceUtcTicks == right.SequenceUtcTicks &&
        left.Version == right.Version &&
        left.ContentHash == right.ContentHash &&
        left.SnapshotFormatVersion == right.SnapshotFormatVersion &&
        left.SnapshotJson == right.SnapshotJson;

    private static ArchiveDomainFactRow CloneDomainRow(
        ArchiveDomainFactRow source) =>
        new()
        {
            DomainKind = source.DomainKind,
            EntityId = source.EntityId,
            GameAccountId = source.GameAccountId,
            ArchiveId = source.ArchiveId,
            TypeKey = source.TypeKey,
            SequenceUtcTicks = source.SequenceUtcTicks,
            Version = source.Version,
            ContentHash = source.ContentHash,
            SnapshotFormatVersion = source.SnapshotFormatVersion,
            SnapshotJson = source.SnapshotJson,
        };

    private static ArchiveOperationResult DomainConflict(string reason) =>
        new(
            ChangeExecutionStatus.Conflict,
            ChangeSetId: null,
            AffectedRecordCount: 0,
            reason);

    private static string ComputeDomainHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

    private static void ValidateAccountId(Guid gameAccountId)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("Game account ID cannot be empty.");
        }
    }
    private static string SerializeDomainSnapshot(
        ArchiveDomainFactRow? row) =>
        row is null
            ? string.Empty
            : JsonSerializer.Serialize(row, DomainJsonOptions);

    private static ArchiveDomainFactRow? DeserializeDomainSnapshot(
        string? json) =>
        string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<ArchiveDomainFactRow>(
                json,
                DomainJsonOptions) ??
              throw new InvalidDataException(
                  "A domain snapshot could not be deserialized.");

    private static ItemStateObservation DeserializeItemObservation(
        string json) =>
        (JsonSerializer.Deserialize<ItemObservationPayload>(
            json,
            DomainJsonOptions) ??
         throw new InvalidDataException("Invalid item observation payload."))
        .ToDomain();

    private static ManualAcquisition DeserializeManualAcquisition(
        string json) =>
        (JsonSerializer.Deserialize<ManualAcquisitionPayload>(
            json,
            DomainJsonOptions) ??
         throw new InvalidDataException("Invalid manual acquisition payload."))
        .ToDomain();

    private static ChallengeObservation DeserializeChallengeObservation(
        string json) =>
        (JsonSerializer.Deserialize<ChallengeObservationPayload>(
            json,
            DomainJsonOptions) ??
         throw new InvalidDataException(
             "Invalid challenge observation payload."))
        .ToDomain();

    private static ChallengeObservationPayload DeserializeChallengePayload(
        string json) =>
        JsonSerializer.Deserialize<ChallengeObservationPayload>(
            json,
            DomainJsonOptions) ??
        throw new InvalidDataException(
            "Invalid challenge observation payload.");

    private static bool ChallengeBusinessContentEquals(
        ChallengeObservationPayload left,
        ChallengeObservationPayload right) =>
        left.Mode == right.Mode &&
        left.SeasonId == right.SeasonId &&
        left.PeriodStart == right.PeriodStart &&
        left.PeriodEnd == right.PeriodEnd &&
        left.Completeness == right.Completeness &&
        JsonSerializer.Serialize(left.Floors, DomainJsonOptions) ==
        JsonSerializer.Serialize(right.Floors, DomainJsonOptions);

    private sealed record DomainCommitEnvelope(
        OperationId OperationId,
        DataChangeOperationKind OperationKind,
        DataOrigin Origin,
        string Summary,
        DateTimeOffset StartedAt,
        DateTimeOffset CommittedAt,
        bool CaptureUndo,
        Guid? CleanupConfirmationId)
    {
        public static DomainCommitEnvelope From(
            ItemObservationCommitRequest request) =>
            new(
                request.OperationId,
                request.OperationKind,
                request.Origin,
                request.Summary,
                request.StartedAt,
                request.CommittedAt,
                request.CaptureUndo,
                request.CleanupConfirmationId);

        public static DomainCommitEnvelope From(
            ManualAcquisitionCommitRequest request) =>
            new(
                request.OperationId,
                request.OperationKind,
                DataOrigin.UserEntered,
                request.Summary,
                request.StartedAt,
                request.CommittedAt,
                request.CaptureUndo,
                request.CleanupConfirmationId);

        public static DomainCommitEnvelope From(
            ChallengeObservationCommitRequest request) =>
            new(
                request.OperationId,
                request.OperationKind,
                request.Origin,
                request.Summary,
                request.StartedAt,
                request.CommittedAt,
                request.CaptureUndo,
                request.CleanupConfirmationId);
    }

    private sealed record DomainProposal(
        string DomainKind,
        string EntityId,
        Guid GameAccountId,
        string? TypeKey,
        long? SequenceUtcTicks,
        string? PayloadJson,
        FactVersion? ExpectedVersion,
        bool IsImmutable,
        bool IsDelete)
    {
        public static DomainProposal Immutable(
            string kind,
            string id,
            Guid accountId,
            string? typeKey,
            long? sequence,
            string payload) =>
            new(
                kind,
                id,
                accountId,
                typeKey,
                sequence,
                payload,
                null,
                IsImmutable: true,
                IsDelete: false);

        public static DomainProposal Mutable(
            string kind,
            string id,
            Guid accountId,
            string? typeKey,
            long? sequence,
            string payload,
            FactVersion? expectedVersion) =>
            new(
                kind,
                id,
                accountId,
                typeKey,
                sequence,
                payload,
                expectedVersion,
                IsImmutable: false,
                IsDelete: false);

        public static DomainProposal Delete(
            string kind,
            string id,
            Guid accountId,
            FactVersion? expectedVersion) =>
            new(
                kind,
                id,
                accountId,
                null,
                null,
                null,
                expectedVersion,
                IsImmutable: false,
                IsDelete: true);

        public ArchiveDomainFactRow ToRow(
            Guid archiveId,
            long version,
            string hash) =>
            new()
            {
                DomainKind = DomainKind,
                EntityId = EntityId,
                GameAccountId = GameAccountId.ToString("D"),
                ArchiveId = archiveId.ToString("D"),
                TypeKey = TypeKey,
                SequenceUtcTicks = SequenceUtcTicks,
                Version = version,
                ContentHash = hash,
                SnapshotFormatVersion = DomainSnapshotFormatVersion,
                SnapshotJson = PayloadJson!,
            };
    }

    private sealed record PreparedDomainMutation(
        Guid ArchiveId,
        ArchiveDomainFactRow? Before,
        ArchiveDomainFactRow? After,
        EntityChangeKind ChangeKind)
    {
        public string? BeforeSnapshotJson =>
            Before is null ? null : SerializeDomainSnapshot(Before);
        public string? AfterSnapshotJson =>
            After is null ? null : SerializeDomainSnapshot(After);

        public static PreparedDomainMutation Insert(
            Guid archiveId,
            ArchiveDomainFactRow after) =>
            new(archiveId, null, after, EntityChangeKind.Insert);

        public static PreparedDomainMutation Update(
            Guid archiveId,
            ArchiveDomainFactRow before,
            ArchiveDomainFactRow after) =>
            new(archiveId, before, after, EntityChangeKind.Update);

        public static PreparedDomainMutation Delete(
            Guid archiveId,
            ArchiveDomainFactRow before) =>
            new(archiveId, before, null, EntityChangeKind.Delete);
    }

    private sealed class ArchiveDomainFactRow
    {
        public string DomainKind { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string GameAccountId { get; set; } = string.Empty;
        public string ArchiveId { get; set; } = string.Empty;
        public string? TypeKey { get; set; }
        public long? SequenceUtcTicks { get; set; }
        public long Version { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public int SnapshotFormatVersion { get; set; }
        public string SnapshotJson { get; set; } = string.Empty;
    }

    private sealed class ArchiveDomainRevisionRow
    {
        public string RevisionId { get; set; } = string.Empty;
        public string ChangeSetId { get; set; } = string.Empty;
        public string DomainKind { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string GameAccountId { get; set; } = string.Empty;
        public int ChangeKind { get; set; }
        public long? BeforeVersion { get; set; }
        public long? AfterVersion { get; set; }
        public int SnapshotFormatVersion { get; set; }
        public string? BeforeSnapshotJson { get; set; }
        public string? AfterSnapshotJson { get; set; }
        public long CreatedAtUtcTicks { get; set; }
    }

    private sealed class DomainIdentityRow
    {
        public string DomainKind { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
    }

    private sealed record PurgeDomainFact(
        string TombstoneKey,
        long VersionHighWater);
    private sealed record ItemObservationPayload(
        Guid ObservationId,
        Guid GameAccountId,
        int Kind,
        int CollectionCompleteness,
        InventoryEntryPayload[] Entries,
        ProvenancePayload Provenance)
    {
        public static ItemObservationPayload FromDomain(
            ItemStateObservation observation) =>
            new(
                observation.ObservationId.Value,
                observation.GameAccountId,
                (int)observation.Kind,
                (int)observation.CollectionCompleteness,
                observation.Entries
                    .OrderBy(entry => entry.StableItemKey)
                    .Select(InventoryEntryPayload.FromDomain)
                    .ToArray(),
                ProvenancePayload.FromDomain(observation.Provenance));

        public ItemStateObservation ToDomain() =>
            new(
                new ItemObservationId(ObservationId),
                GameAccountId,
                (InventoryItemKind)Kind,
                (DataCompleteness)CollectionCompleteness,
                Entries.Select(entry => entry.ToDomain()),
                Provenance.ToDomain());
    }

    private sealed record InventoryEntryPayload(
        int Kind,
        string TypeId,
        string? InstanceId,
        int? Quantity,
        int? Level,
        int? Progression,
        int FieldCoverage,
        int FieldCompleteness)
    {
        public static InventoryEntryPayload FromDomain(
            InventoryStateEntry entry) =>
            new(
                (int)entry.Kind,
                entry.TypeId,
                entry.InstanceId,
                entry.Quantity,
                entry.Level,
                entry.Progression,
                (int)entry.FieldCoverage,
                (int)entry.FieldCompleteness);

        public InventoryStateEntry ToDomain() =>
            new(
                (InventoryItemKind)Kind,
                TypeId,
                InstanceId,
                Quantity,
                Level,
                Progression,
                (InventoryFieldCoverage)FieldCoverage,
                (DataCompleteness)FieldCompleteness);
    }

    private sealed record ManualAcquisitionPayload(
        Guid AcquisitionId,
        Guid GameAccountId,
        int Kind,
        string TypeId,
        OccurrencePayload OccurredAt,
        int Reason,
        string? ReasonDetail,
        ProvenancePayload Provenance)
    {
        public static ManualAcquisitionPayload FromDomain(
            ManualAcquisition acquisition) =>
            new(
                acquisition.AcquisitionId.Value,
                acquisition.GameAccountId,
                (int)acquisition.Kind,
                acquisition.TypeId,
                OccurrencePayload.FromDomain(acquisition.OccurredAt),
                (int)acquisition.Reason,
                acquisition.ReasonDetail,
                ProvenancePayload.FromDomain(acquisition.Provenance));

        public ManualAcquisition ToDomain() =>
            new(
                new ManualAcquisitionId(AcquisitionId),
                GameAccountId,
                (InventoryItemKind)Kind,
                TypeId,
                OccurredAt.ToDomain(),
                (ManualAcquisitionReason)Reason,
                ReasonDetail,
                Provenance.ToDomain());
    }

    private sealed record ChallengeObservationPayload(
        Guid ObservationId,
        Guid GameAccountId,
        int Mode,
        string SeasonId,
        OffsetDateTimePayload? PeriodStart,
        OffsetDateTimePayload? PeriodEnd,
        int Completeness,
        SpiralAbyssFloorPayload[] Floors,
        ProvenancePayload Provenance)
    {
        public static ChallengeObservationPayload FromDomain(
            ChallengeObservation observation) =>
            new(
                observation.ObservationId.Value,
                observation.GameAccountId,
                (int)observation.Mode,
                observation.SeasonId,
                OffsetDateTimePayload.FromNullable(observation.PeriodStart),
                OffsetDateTimePayload.FromNullable(observation.PeriodEnd),
                (int)observation.Completeness,
                observation.SpiralAbyssFloors
                    .Select(SpiralAbyssFloorPayload.FromDomain)
                    .ToArray(),
                ProvenancePayload.FromDomain(observation.Provenance));

        public ChallengeObservation ToDomain() =>
            new(
                new ChallengeObservationId(ObservationId),
                GameAccountId,
                (ChallengeMode)Mode,
                SeasonId,
                PeriodStart?.ToDomain(),
                PeriodEnd?.ToDomain(),
                (DataCompleteness)Completeness,
                Floors.Select(floor => floor.ToDomain()),
                Provenance.ToDomain());
    }

    private sealed record SpiralAbyssFloorPayload(
        int Floor,
        int Completeness,
        SpiralAbyssChamberPayload[] Chambers)
    {
        public static SpiralAbyssFloorPayload FromDomain(
            SpiralAbyssFloorResult floor) =>
            new(
                floor.Floor,
                (int)floor.Completeness,
                floor.Chambers
                    .Select(SpiralAbyssChamberPayload.FromDomain)
                    .ToArray());

        public SpiralAbyssFloorResult ToDomain() =>
            new(
                Floor,
                (DataCompleteness)Completeness,
                Chambers.Select(chamber => chamber.ToDomain()));
    }

    private sealed record SpiralAbyssChamberPayload(
        int Chamber,
        int Stars,
        OccurrencePayload CompletedAt)
    {
        public static SpiralAbyssChamberPayload FromDomain(
            SpiralAbyssChamberResult chamber) =>
            new(
                chamber.Chamber,
                chamber.Stars,
                OccurrencePayload.FromDomain(chamber.CompletedAt));

        public SpiralAbyssChamberResult ToDomain() =>
            new(Chamber, Stars, CompletedAt.ToDomain());
    }
    private sealed record OccurrencePayload(
        int Kind,
        OffsetDateTimePayload? Start,
        OffsetDateTimePayload? End,
        int? Precision)
    {
        public static OccurrencePayload FromDomain(OccurrenceTime value) =>
            new(
                (int)value.Kind,
                OffsetDateTimePayload.FromNullable(value.Start),
                OffsetDateTimePayload.FromNullable(value.End),
                value.Precision is null ? null : (int)value.Precision.Value);

        public OccurrenceTime ToDomain()
        {
            OccurrenceTimeKind kind = (OccurrenceTimeKind)Kind;
            return kind switch
            {
                OccurrenceTimeKind.Unknown => OccurrenceTime.Unknown,
                OccurrenceTimeKind.Instant => OccurrenceTime.At(
                    Start!.ToDomain(),
                    (TimePrecision)Precision!.Value),
                OccurrenceTimeKind.Interval => OccurrenceTime.ForInterval(
                    Start!.ToDomain(),
                    End!.ToDomain(),
                    (TimePrecision)Precision!.Value),
                OccurrenceTimeKind.UncertainInterval => OccurrenceTime.Within(
                    Start!.ToDomain(),
                    End!.ToDomain(),
                    (TimePrecision)Precision!.Value),
                _ => throw new InvalidDataException(
                    "Unsupported occurrence time kind."),
            };
        }
    }

    private sealed record ProvenancePayload(
        int Origin,
        OffsetDateTimePayload? ObservedAt,
        OffsetDateTimePayload? FetchedAt,
        OffsetDateTimePayload? ImportedAt,
        string? Provider,
        string? SourceRecordId,
        string? SourceSnapshotId,
        Guid? AcquisitionBatchId)
    {
        public static ProvenancePayload FromDomain(RecordProvenance value) =>
            new(
                (int)value.Origin,
                OffsetDateTimePayload.FromNullable(
                    value.Timestamps.ObservedAt),
                OffsetDateTimePayload.FromNullable(
                    value.Timestamps.FetchedAt),
                OffsetDateTimePayload.FromNullable(
                    value.Timestamps.ImportedAt),
                value.Source?.Provider,
                value.Source?.SourceRecordId,
                value.Source?.SourceSnapshotId,
                value.AcquisitionBatchId?.Value);

        public RecordProvenance ToDomain()
        {
            DataSourceReference? source =
                Provider is null && SourceRecordId is null &&
                SourceSnapshotId is null
                    ? null
                    : new DataSourceReference(
                        Provider,
                        SourceRecordId,
                        SourceSnapshotId);
            return new RecordProvenance(
                (DataOrigin)Origin,
                new RecordTimestamps(
                    ObservedAt?.ToDomain(),
                    FetchedAt?.ToDomain(),
                    ImportedAt?.ToDomain()),
                source,
                AcquisitionBatchId is null
                    ? null
                    : new AcquisitionBatchId(AcquisitionBatchId.Value));
        }
    }

    private sealed record OffsetDateTimePayload(
        long UtcTicks,
        int OffsetMinutes)
    {
        public static OffsetDateTimePayload? FromNullable(
            DateTimeOffset? value) =>
            value is null
                ? null
                : new OffsetDateTimePayload(
                    value.Value.UtcDateTime.Ticks,
                    checked((int)value.Value.Offset.TotalMinutes));

        public DateTimeOffset ToDomain() =>
            new DateTimeOffset(UtcTicks, TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(OffsetMinutes));
    }
}
