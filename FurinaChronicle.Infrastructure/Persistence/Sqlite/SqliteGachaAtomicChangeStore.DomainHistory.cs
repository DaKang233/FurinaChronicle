// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
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

    public Task<ArchiveOperationResult> ApplyArchiveDomainPortableAsync(
        ArchiveDomainPortableApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Package.FormatMajor != 1 ||
            request.Plan.ConflictCount != 0 ||
            request.Package.DatasetKind != request.Plan.DatasetKind ||
            !string.Equals(
                request.Package.PackageFingerprint,
                request.Plan.PackageFingerprint,
                StringComparison.Ordinal))
        {
            return Task.FromResult(DomainConflict(
                "The Portable package or plan is incompatible or contains conflicts."));
        }

        Dictionary<Guid, ArchiveDomainPortableAccount> sources =
            request.Package.Accounts.ToDictionary(
                account => account.SourceAccountId);
        var accountExpectations = new List<PortableAccountExpectation>();
        var proposals = new List<DomainProposal>();
        var portableRevisions = new List<PortableManualRevision>();
        foreach (ArchiveDomainPortableAccountPlan accountPlan in
                 request.Plan.Accounts)
        {
            if (!sources.TryGetValue(
                    accountPlan.SourceAccountId,
                    out ArchiveDomainPortableAccount? source))
            {
                return Task.FromResult(DomainConflict(
                    "The Portable plan references an account absent from the package."));
            }
            DateTimeOffset now = request.ReceivedAt;
            var targetIdentity = new GameRoleIdentity(
                accountPlan.TargetRoleIdentityId,
                source.RoleIdentity.NaturalIdentity);
            var targetAccount = new GameAccount(
                accountPlan.TargetAccountId,
                request.Plan.TargetArchiveId,
                source.RoleIdentity.NaturalIdentity.Uid,
                ResolvePortableServerRegion(
                    source.RoleIdentity.NaturalIdentity.Server),
                DisplayName: null,
                IsPlaceholder: false,
                now,
                now,
                targetIdentity);
            accountExpectations.Add(new PortableAccountExpectation(
                targetAccount,
                accountPlan.CreateAccount));

            foreach (ItemStateObservation observation in
                     source.ItemObservations)
            {
                ItemStateObservation mapped = new(
                    observation.ObservationId,
                    accountPlan.TargetAccountId,
                    observation.Kind,
                    observation.CollectionCompleteness,
                    observation.Entries,
                    observation.Provenance);
                proposals.Add(DomainProposal.Immutable(
                    ItemObservationEntityKind,
                    mapped.ObservationId.ToString(),
                    mapped.GameAccountId,
                    ((int)mapped.Kind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    mapped.Provenance.Timestamps.ObservedAt?
                        .UtcDateTime.Ticks,
                    JsonSerializer.Serialize(
                        ItemObservationPayload.FromDomain(mapped),
                        DomainJsonOptions)));
            }
            foreach (ManualAcquisition acquisition in
                     source.ManualAcquisitions)
            {
                ManualAcquisition mapped = MapAcquisition(
                    acquisition,
                    accountPlan.TargetAccountId);
                proposals.Add(DomainProposal.Mutable(
                    ManualAcquisitionEntityKind,
                    mapped.AcquisitionId.ToString(),
                    mapped.GameAccountId,
                    mapped.TypeId,
                    mapped.OccurredAt.UtcStart?.UtcDateTime.Ticks,
                    JsonSerializer.Serialize(
                        ManualAcquisitionPayload.FromDomain(mapped),
                        DomainJsonOptions),
                    expectedVersion: null));
            }
            foreach (ChallengeObservation observation in
                     source.ChallengeObservations)
            {
                if (observation.Mode != ChallengeMode.SpiralAbyss)
                {
                    return Task.FromResult(DomainConflict(
                        "The Portable package contains an unsupported challenge mode."));
                }
                ChallengeObservation mapped = new(
                    observation.ObservationId,
                    accountPlan.TargetAccountId,
                    observation.Mode,
                    observation.SeasonId,
                    observation.PeriodStart,
                    observation.PeriodEnd,
                    observation.Completeness,
                    observation.SpiralAbyssFloors,
                    observation.Provenance);
                proposals.Add(DomainProposal.Immutable(
                    ChallengeObservationEntityKind,
                    mapped.ObservationId.ToString(),
                    mapped.GameAccountId,
                    $"{(int)mapped.Mode}:{mapped.SeasonId}",
                    mapped.Provenance.Timestamps.ObservedAt?
                        .UtcDateTime.Ticks,
                    JsonSerializer.Serialize(
                        ChallengeObservationPayload.FromDomain(mapped),
                        DomainJsonOptions)));
            }
            portableRevisions.AddRange(source.ManualRevisions.Select(
                revision => new PortableManualRevision(
                    accountPlan.TargetAccountId,
                    revision with
                    {
                        Before = revision.Before is null
                            ? null
                            : MapAcquisition(
                                revision.Before,
                                accountPlan.TargetAccountId),
                        After = revision.After is null
                            ? null
                            : MapAcquisition(
                                revision.After,
                                accountPlan.TargetAccountId),
                    })));
        }
        if (proposals.Count == 0)
        {
            return Task.FromResult(DomainConflict(
                "A Portable package must contain at least one supported fact."));
        }

        PlayerArchive? archiveToCreate = request.Plan.CreateArchive
            ? new PlayerArchive(
                request.Plan.TargetArchiveId,
                request.Package.SourceArchiveName,
                request.ReceivedAt,
                request.ReceivedAt)
            : null;
        var portable = new PortableApplyContext(
            request.Package.DatasetKind,
            request.Package.PackageFingerprint,
            request.Plan.TargetArchiveId,
            request.ReceivedAt,
            archiveToCreate,
            accountExpectations,
            portableRevisions,
            request.Plan.AddCount,
            request.Plan.SkipCount);
        var envelope = new DomainCommitEnvelope(
            request.OperationId,
            DataChangeOperationKind.Import,
            DataOrigin.FurinaImport,
            request.Summary,
            request.ReceivedAt,
            request.ReceivedAt,
            request.CaptureUndo,
            request.CleanupConfirmationId,
            portable);
        return CommitDomainAsync(envelope, proposals, cancellationToken);
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

    public async Task<ArchiveDomainPage<ItemStateObservation>>
        GetItemObservationPageAsync(
            ItemObservationPageQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePageQuery(query.GameAccountId, query.Offset, query.Limit);
        (string where, object[] arguments) = BuildPagedWhere(
            query.GameAccountId,
            ItemObservationEntityKind,
            query.Kind is null
                ? null
                : ((int)query.Kind.Value).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            query.ObservedFrom,
            query.ObservedTo,
            typeKeyPrefix: false);
        await database.InitializeAsync(cancellationToken);
        int total = await database.Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM ArchiveDomainFacts WHERE {where};",
            arguments);
        List<ArchiveDomainFactRow> rows = await database.Connection
            .QueryAsync<ArchiveDomainFactRow>(
                $"SELECT * FROM ArchiveDomainFacts WHERE {where} " +
                "ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks DESC, EntityId LIMIT ? OFFSET ?;",
                [.. arguments, query.Limit, query.Offset]);
        return new ArchiveDomainPage<ItemStateObservation>(
            rows.Select(row => DeserializeItemObservation(row.SnapshotJson))
                .ToArray(),
            query.Offset,
            query.Limit,
            total);
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

    public async Task<ArchiveDomainPage<ManualAcquisitionState>>
        GetManualAcquisitionPageAsync(
            ManualAcquisitionPageQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePageQuery(query.GameAccountId, query.Offset, query.Limit);
        (string where, object[] arguments) = BuildPagedWhere(
            query.GameAccountId,
            ManualAcquisitionEntityKind,
            typeKey: null,
            query.OccurredFrom,
            query.OccurredTo,
            typeKeyPrefix: false,
            payloadKind: query.Kind is null ? null : (int)query.Kind.Value);
        await database.InitializeAsync(cancellationToken);
        int total = await database.Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM ArchiveDomainFacts WHERE {where};",
            arguments);
        List<ArchiveDomainFactRow> rows = await database.Connection
            .QueryAsync<ArchiveDomainFactRow>(
                $"SELECT * FROM ArchiveDomainFacts WHERE {where} " +
                "ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks DESC, EntityId LIMIT ? OFFSET ?;",
                [.. arguments, query.Limit, query.Offset]);
        return new ArchiveDomainPage<ManualAcquisitionState>(
            rows.Select(row => new ManualAcquisitionState(
                    DeserializeManualAcquisition(row.SnapshotJson),
                    new FactVersion(row.Version),
                    ParseRequiredGuid(row.ArchiveId, "archive")))
                .ToArray(),
            query.Offset,
            query.Limit,
            total);
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

    public async Task<ArchiveDomainPage<ChallengeObservation>>
        GetChallengeObservationPageAsync(
            ChallengeObservationPageQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePageQuery(query.GameAccountId, query.Offset, query.Limit);
        string? typeKey = query.SeasonId is null
            ? query.Mode is null
                ? null
                : $"{(int)query.Mode.Value}:"
            : $"{(int)(query.Mode ?? ChallengeMode.SpiralAbyss)}:{query.SeasonId.Trim()}";
        (string where, object[] arguments) = BuildPagedWhere(
            query.GameAccountId,
            ChallengeObservationEntityKind,
            typeKey,
            query.ObservedFrom,
            query.ObservedTo,
            typeKeyPrefix: query.SeasonId is null && query.Mode is not null);
        await database.InitializeAsync(cancellationToken);
        int total = await database.Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM ArchiveDomainFacts WHERE {where};",
            arguments);
        List<ArchiveDomainFactRow> rows = await database.Connection
            .QueryAsync<ArchiveDomainFactRow>(
                $"SELECT * FROM ArchiveDomainFacts WHERE {where} " +
                "ORDER BY SequenceUtcTicks IS NULL, SequenceUtcTicks DESC, EntityId LIMIT ? OFFSET ?;",
                [.. arguments, query.Limit, query.Offset]);
        return new ArchiveDomainPage<ChallengeObservation>(
            rows.Select(row => DeserializeChallengeObservation(
                    row.SnapshotJson))
                .ToArray(),
            query.Offset,
            query.Limit,
            total);
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

    public async Task<ChallengeObservation?>
        GetCurrentChallengeEvidenceAsync(
            Guid gameAccountId,
            ChallengeMode mode,
            string seasonId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seasonId);
        IReadOnlyList<ChallengeObservation> observations =
            await GetChallengeObservationsAsync(
                gameAccountId,
                mode,
                cancellationToken);
        return observations
            .Where(item => item.SeasonId == seasonId)
            .OrderByDescending(item => item.Completeness)
            .ThenByDescending(item =>
                item.Provenance.Timestamps.ObservedAt?.UtcDateTime.Ticks ??
                long.MinValue)
            .ThenByDescending(item => item.ObservationId.Value)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<ManualAcquisitionRevisionItem>>
        GetManualAcquisitionRevisionsAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(gameAccountId);
        await database.InitializeAsync(cancellationToken);
        List<ArchiveDomainRevisionRow> rows = await database.Connection
            .QueryAsync<ArchiveDomainRevisionRow>(
                """
                SELECT * FROM ArchiveDomainRevisions
                WHERE GameAccountId = ? AND DomainKind = ?
                ORDER BY CreatedAtUtcTicks, rowid;
                """,
                gameAccountId.ToString("D"),
                ManualAcquisitionEntityKind);
        return rows.Select(row => new ManualAcquisitionRevisionItem(
                ParseRequiredGuid(row.RevisionId, "revision"),
                ParseRequiredGuid(row.ChangeSetId, "change set"),
                (EntityChangeKind)row.ChangeKind,
                row.BeforeVersion is null
                    ? null
                    : new FactVersion(row.BeforeVersion.Value),
                row.AfterVersion is null
                    ? null
                    : new FactVersion(row.AfterVersion.Value),
                DeserializeAcquisitionFromDomainSnapshot(
                    row.BeforeSnapshotJson),
                DeserializeAcquisitionFromDomainSnapshot(
                    row.AfterSnapshotJson),
                new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero)))
            .ToArray();
    }

    internal async Task<ItemStateObservation?> FindItemObservationAsync(
        Guid gameAccountId,
        ItemObservationId observationId,
        CancellationToken cancellationToken = default)
    {
        ArchiveDomainFactRow? row = await FindDomainFactAsync(
            gameAccountId,
            ItemObservationEntityKind,
            observationId.ToString(),
            cancellationToken);
        return row is null
            ? null
            : DeserializeItemObservation(row.SnapshotJson);
    }

    internal async Task<ManualAcquisitionState?> FindManualAcquisitionAsync(
        Guid gameAccountId,
        ManualAcquisitionId acquisitionId,
        CancellationToken cancellationToken = default)
    {
        ArchiveDomainFactRow? row = await FindDomainFactAsync(
            gameAccountId,
            ManualAcquisitionEntityKind,
            acquisitionId.ToString(),
            cancellationToken);
        return row is null
            ? null
            : new ManualAcquisitionState(
                DeserializeManualAcquisition(row.SnapshotJson),
                new FactVersion(row.Version),
                ParseRequiredGuid(row.ArchiveId, "archive"));
    }

    internal async Task<ChallengeObservation?> FindChallengeObservationAsync(
        Guid gameAccountId,
        ChallengeObservationId observationId,
        CancellationToken cancellationToken = default)
    {
        ArchiveDomainFactRow? row = await FindDomainFactAsync(
            gameAccountId,
            ChallengeObservationEntityKind,
            observationId.ToString(),
            cancellationToken);
        return row is null
            ? null
            : DeserializeChallengeObservation(row.SnapshotJson);
    }

    internal async Task<ManualAcquisitionRevisionItem?>
        FindManualAcquisitionRevisionAsync(
            Guid gameAccountId,
            Guid revisionId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(gameAccountId);
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Revision ID cannot be empty.",
                nameof(revisionId));
        }
        await database.InitializeAsync(cancellationToken);
        ArchiveDomainRevisionRow? row = (await database.Connection
                .QueryAsync<ArchiveDomainRevisionRow>(
                    """
                    SELECT * FROM ArchiveDomainRevisions
                    WHERE GameAccountId = ? AND DomainKind = ?
                          AND RevisionId = ?;
                    """,
                    gameAccountId.ToString("D"),
                    ManualAcquisitionEntityKind,
                    revisionId.ToString("D")))
            .SingleOrDefault();
        return row is null
            ? null
            : new ManualAcquisitionRevisionItem(
                ParseRequiredGuid(row.RevisionId, "revision"),
                ParseRequiredGuid(row.ChangeSetId, "change set"),
                (EntityChangeKind)row.ChangeKind,
                row.BeforeVersion is null
                    ? null
                    : new FactVersion(row.BeforeVersion.Value),
                row.AfterVersion is null
                    ? null
                    : new FactVersion(row.AfterVersion.Value),
                DeserializeAcquisitionFromDomainSnapshot(
                    row.BeforeSnapshotJson),
                DeserializeAcquisitionFromDomainSnapshot(
                    row.AfterSnapshotJson),
                new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero));
    }

    private async Task<ArchiveDomainFactRow?> FindDomainFactAsync(
        Guid gameAccountId,
        string domainKind,
        string entityId,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(gameAccountId);
        await database.InitializeAsync(cancellationToken);
        return (await database.Connection.QueryAsync<ArchiveDomainFactRow>(
                """
                SELECT * FROM ArchiveDomainFacts
                WHERE GameAccountId = ? AND DomainKind = ? AND EntityId = ?;
                """,
                gameAccountId.ToString("D"),
                domainKind,
                entityId))
            .SingleOrDefault();
    }

    private static void ValidatePageQuery(
        Guid accountId,
        int offset,
        int limit)
    {
        ValidateAccountId(accountId);
        if (offset < 0 || limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "History pages require a non-negative offset and a limit from 1 through 500.");
        }
    }

    private static (string Where, object[] Arguments) BuildPagedWhere(
        Guid accountId,
        string domainKind,
        string? typeKey,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool typeKeyPrefix,
        int? payloadKind = null)
    {
        if (from is not null && to is not null &&
            to.Value.ToUniversalTime() <= from.Value.ToUniversalTime())
        {
            throw new ArgumentException(
                "The history time range must have an end after its start.");
        }
        var clauses = new List<string>
        {
            "GameAccountId = ?",
            "DomainKind = ?",
        };
        var arguments = new List<object>
        {
            accountId.ToString("D"),
            domainKind,
        };
        if (typeKey is not null)
        {
            clauses.Add(typeKeyPrefix ? "TypeKey LIKE ?" : "TypeKey = ?");
            arguments.Add(typeKeyPrefix ? typeKey + "%" : typeKey);
        }
        if (payloadKind is not null)
        {
            clauses.Add("json_extract(SnapshotJson, '$.kind') = ?");
            arguments.Add(payloadKind.Value);
        }
        if (from is not null)
        {
            clauses.Add("SequenceUtcTicks >= ?");
            arguments.Add(from.Value.UtcDateTime.Ticks);
        }
        if (to is not null)
        {
            clauses.Add("SequenceUtcTicks < ?");
            arguments.Add(to.Value.UtcDateTime.Ticks);
        }
        return (string.Join(" AND ", clauses), arguments.ToArray());
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

            if (envelope.Portable is not null)
            {
                int receiptCount = connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*) FROM DomainPortableReceipts
                    WHERE DatasetKind = ? AND PackageFingerprint = ?
                      AND TargetArchiveId = ?;
                    """,
                    envelope.Portable.DatasetKind.ToString(),
                    envelope.Portable.PackageFingerprint,
                    envelope.Portable.TargetArchiveId.ToString("D"));
                if (receiptCount > 0)
                {
                    InsertDomainCommitResult(
                        connection,
                        envelope,
                        changeSetId: null,
                        ChangeExecutionStatus.NoOp,
                        affectedRecordCount: 0);
                    result = new ArchiveOperationResult(
                        ChangeExecutionStatus.NoOp,
                        ChangeSetId: null,
                        AffectedRecordCount: 0);
                    return;
                }
                if (!TryValidatePortableParents(
                        connection,
                        envelope.Portable,
                        out string? parentConflict))
                {
                    result = DomainConflict(parentConflict!);
                    return;
                }
                if (!TryValidatePortableRevisions(
                        connection,
                        envelope.Portable,
                        out string? revisionConflict))
                {
                    result = DomainConflict(revisionConflict!);
                    return;
                }
            }

            var prepared = new List<PreparedDomainMutation>();
            int suppressed = 0;
            int skipped = 0;
            foreach (DomainProposal proposal in proposals)
            {
                Guid archiveId = envelope.Portable?.TargetArchiveId ??
                    ReadArchiveId(connection, proposal.GameAccountId);
                string? roleIdentityId = envelope.Portable?
                    .FindAccount(proposal.GameAccountId)?
                    .Account.RoleIdentity?.Id.ToString();
                if (ReadActiveDomainTombstone(
                        connection,
                        archiveId,
                        proposal,
                        roleIdentityId) is not null)
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
                    if (current is not null &&
                        ChallengeBusinessContentEquals(
                            DeserializeChallengePayload(current.SnapshotJson),
                            challenge))
                    {
                        payload = string.Empty;
                    }
                    ArchiveDomainFactRow[] related = connection
                        .Query<ArchiveDomainFactRow>(
                            """
                            SELECT * FROM ArchiveDomainFacts
                            WHERE GameAccountId = ? AND DomainKind = ?
                              AND TypeKey = ? AND EntityId <> ?;
                            """,
                            proposal.GameAccountId.ToString("D"),
                            proposal.DomainKind,
                            proposal.TypeKey,
                            proposal.EntityId)
                        .ToArray();
                    string? sourceRecordId =
                        challenge.Provenance.SourceRecordId;
                    if (payload.Length != 0 &&
                        !string.IsNullOrWhiteSpace(sourceRecordId))
                    {
                        foreach (ArchiveDomainFactRow candidate in related)
                        {
                            ChallengeObservationPayload existing =
                                DeserializeChallengePayload(
                                    candidate.SnapshotJson);
                            if (!string.Equals(
                                    existing.Provenance.SourceRecordId,
                                    sourceRecordId,
                                    StringComparison.Ordinal))
                            {
                                continue;
                            }
                            if (ChallengeBusinessContentEquals(
                                    existing,
                                    challenge))
                            {
                                payload = string.Empty;
                                break;
                            }
                            result = DomainConflict(
                                "A stable challenge source record ID already exists with different business content.");
                            return;
                        }
                    }
                    if (proposal.SequenceUtcTicks is long sequence)
                    {
                        ArchiveDomainFactRow[] sameTime = related
                            .Where(item =>
                                item.SequenceUtcTicks == sequence)
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
                            skipped++;
                            continue;
                        }
                        ArchiveDomainFactRow? previous = related
                            .Where(item => item.SequenceUtcTicks < sequence)
                            .OrderByDescending(item => item.SequenceUtcTicks)
                            .ThenByDescending(item => item.EntityId)
                            .FirstOrDefault();
                        ArchiveDomainFactRow? next = related
                            .Where(item => item.SequenceUtcTicks > sequence)
                            .OrderBy(item => item.SequenceUtcTicks)
                            .ThenBy(item => item.EntityId)
                            .FirstOrDefault();
                        if (new[] { previous, next }
                            .Where(item => item is not null)
                            .Any(item => ChallengeBusinessContentEquals(
                                DeserializeChallengePayload(
                                    item!.SnapshotJson),
                                challenge)))
                        {
                            skipped++;
                            continue;
                        }
                    }
                    else if (payload.Length == 0)
                    {
                        skipped++;
                        continue;
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
                    skipped++;
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
            if (envelope.Portable is not null &&
                (suppressed != 0 ||
                 prepared.Count != envelope.Portable.ExpectedAddCount ||
                 skipped != envelope.Portable.ExpectedSkipCount))
            {
                result = DomainConflict(
                    suppressed != 0
                        ? "The Portable plan contains permanently deleted facts and requires an explicit reintroduction decision."
                        : "The Portable target changed after planning; create a new preview before applying.");
                return;
            }
            int portableRevisionAdds = envelope.Portable is null
                ? 0
                : CountNewPortableRevisions(connection, envelope.Portable);
            if (prepared.Count == 0 && portableRevisionAdds == 0)
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
                .Append(envelope.Portable?.TargetArchiveId ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
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

            if (envelope.Portable is not null)
            {
                InsertPortableParents(connection, envelope.Portable);
            }
            Guid changeSetId = Guid.NewGuid();
            InsertDomainChangeSet(
                connection,
                envelope,
                changeSetId,
                archiveIds,
                prepared.Count);
            bool captureUndo = prepared.Count > 0 &&
                envelope.CaptureUndo &&
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
                    mutation,
                    insertRevision: envelope.Portable is null ||
                        mutation.After?.DomainKind !=
                            ManualAcquisitionEntityKind &&
                        mutation.Before?.DomainKind !=
                            ManualAcquisitionEntityKind ||
                        !envelope.Portable.HasRevisionFor(
                            mutation.After?.EntityId ??
                            mutation.Before!.EntityId));
                if (captureUndo)
                {
                    materialBytes += InsertDomainUndoMaterial(
                        connection,
                        changeSetId,
                        mutation);
                }
            }
            if (envelope.Portable is not null)
            {
                foreach (PortableManualRevision revision in
                         envelope.Portable.ManualRevisions)
                {
                    InsertPortableManualRevision(
                        connection,
                        changeSetId,
                        revision);
                }
                connection.Execute(
                    """
                    INSERT INTO DomainPortableReceipts
                        (ReceiptId, ChangeSetId, DatasetKind,
                         PackageFingerprint, ReceivedAtUtcTicks,
                         ReceivedAtOffsetMinutes, TargetArchiveId, RecordCount)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    Guid.NewGuid().ToString("D"),
                    changeSetId.ToString("D"),
                    envelope.Portable.DatasetKind.ToString(),
                    envelope.Portable.PackageFingerprint,
                    envelope.Portable.ReceivedAt.UtcDateTime.Ticks,
                    checked((int)envelope.Portable.ReceivedAt.Offset.TotalMinutes),
                    envelope.Portable.TargetArchiveId.ToString("D"),
                    prepared.Count);
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
        PreparedDomainMutation mutation,
        bool insertRevision = true)
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
        if (!insertRevision)
        {
            return;
        }
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

    private static bool TryValidatePortableParents(
        SQLiteConnection connection,
        PortableApplyContext portable,
        out string? conflict)
    {
        string archiveId = portable.TargetArchiveId.ToString("D");
        int archiveCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM PlayerArchives WHERE Id = ?;",
            archiveId);
        if (portable.ArchiveToCreate is not null)
        {
            if (archiveCount != 0)
            {
                conflict = "The target archive already exists after planning.";
                return false;
            }
        }
        else if (archiveCount != 1)
        {
            conflict = "The planned target archive no longer exists.";
            return false;
        }

        foreach (PortableAccountExpectation expectation in portable.Accounts)
        {
            GameAccount account = expectation.Account;
            GameRoleIdentity identity = account.RoleIdentity!;
            GameRoleIdentityRow? existingIdentity = connection
                .Query<GameRoleIdentityRow>(
                    "SELECT * FROM GameRoleIdentities WHERE Id = ?;",
                    identity.Id.ToString())
                .SingleOrDefault();
            if (existingIdentity is not null &&
                existingIdentity.ToDomain() != identity)
            {
                conflict =
                    "A shared role identity ID maps to another natural identity.";
                return false;
            }
            GameAccountRow? existingAccount = connection
                .Query<GameAccountRow>(
                    "SELECT * FROM GameAccounts WHERE Id = ?;",
                    account.Id.ToString("D"))
                .SingleOrDefault();
            if (expectation.Create)
            {
                if (existingAccount is not null)
                {
                    conflict =
                        "The planned target account ID is already in use.";
                    return false;
                }
            }
            else if (existingAccount is null ||
                     existingAccount.PlayerArchiveId != archiveId ||
                     existingAccount.GameRoleIdentityId !=
                     identity.Id.ToString())
            {
                conflict =
                    "The planned target account mapping changed after planning.";
                return false;
            }
        }

        conflict = null;
        return true;
    }

    private static void InsertPortableParents(
        SQLiteConnection connection,
        PortableApplyContext portable)
    {
        if (portable.ArchiveToCreate is not null)
        {
            connection.Insert(PlayerArchiveRow.FromDomain(
                portable.ArchiveToCreate));
        }
        foreach (PortableAccountExpectation expectation in portable.Accounts)
        {
            GameAccount account = expectation.Account;
            GameRoleIdentity identity = account.RoleIdentity!;
            int identityCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameRoleIdentities WHERE Id = ?;",
                identity.Id.ToString());
            if (identityCount == 0)
            {
                connection.Insert(GameRoleIdentityRow.FromDomain(identity));
            }
            if (expectation.Create)
            {
                connection.Insert(GameAccountRow.FromDomain(account));
            }
        }
    }

    private static bool TryValidatePortableRevisions(
        SQLiteConnection connection,
        PortableApplyContext portable,
        out string? conflict)
    {
        foreach (PortableManualRevision item in portable.ManualRevisions)
        {
            ArchiveDomainRevisionRow? existing = connection
                .Query<ArchiveDomainRevisionRow>(
                    "SELECT * FROM ArchiveDomainRevisions WHERE RevisionId = ?;",
                    item.Revision.RevisionId.ToString("D"))
                .SingleOrDefault();
            if (existing is not null &&
                !PortableRevisionEquals(existing, portable, item))
            {
                conflict =
                    "A Portable manual revision ID already exists with different content.";
                return false;
            }
        }
        conflict = null;
        return true;
    }

    private static int CountNewPortableRevisions(
        SQLiteConnection connection,
        PortableApplyContext portable) =>
        portable.ManualRevisions.Count(item =>
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ArchiveDomainRevisions WHERE RevisionId = ?;",
                item.Revision.RevisionId.ToString("D")) == 0);

    private static bool PortableRevisionEquals(
        ArchiveDomainRevisionRow existing,
        PortableApplyContext portable,
        PortableManualRevision item)
    {
        PortableManualAcquisitionRevision revision = item.Revision;
        ArchiveDomainFactRow? before = revision.Before is null
            ? null
            : CreatePortableAcquisitionRow(
                revision.Before,
                portable.TargetArchiveId,
                revision.BeforeVersion?.Value ?? 1);
        ArchiveDomainFactRow? after = revision.After is null
            ? null
            : CreatePortableAcquisitionRow(
                revision.After,
                portable.TargetArchiveId,
                revision.AfterVersion?.Value ?? 1);
        ArchiveDomainFactRow identity = after ?? before!;
        return existing.DomainKind == ManualAcquisitionEntityKind &&
            existing.EntityId == identity.EntityId &&
            existing.GameAccountId == item.TargetAccountId.ToString("D") &&
            existing.ChangeKind == (int)revision.ChangeKind &&
            existing.BeforeVersion == revision.BeforeVersion?.Value &&
            existing.AfterVersion == revision.AfterVersion?.Value &&
            existing.BeforeSnapshotJson == (before is null
                ? null
                : SerializeDomainSnapshot(before)) &&
            existing.AfterSnapshotJson == (after is null
                ? null
                : SerializeDomainSnapshot(after)) &&
            existing.CreatedAtUtcTicks == revision.CreatedAt.UtcDateTime.Ticks;
    }

    private static void InsertPortableManualRevision(
        SQLiteConnection connection,
        Guid changeSetId,
        PortableManualRevision portable)
    {
        PortableManualAcquisitionRevision revision = portable.Revision;
        if (connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ArchiveDomainRevisions WHERE RevisionId = ?;",
                revision.RevisionId.ToString("D")) != 0)
        {
            return;
        }
        Guid archiveId = ReadArchiveId(connection, portable.TargetAccountId);
        ArchiveDomainFactRow? before = revision.Before is null
            ? null
            : CreatePortableAcquisitionRow(
                revision.Before,
                archiveId,
                revision.BeforeVersion?.Value ?? 1);
        ArchiveDomainFactRow? after = revision.After is null
            ? null
            : CreatePortableAcquisitionRow(
                revision.After,
                archiveId,
                revision.AfterVersion?.Value ?? 1);
        ArchiveDomainFactRow identity = after ?? before ??
            throw new InvalidDataException(
                "A Portable manual revision has no before or after fact.");
        connection.Execute(
            """
            INSERT INTO ArchiveDomainRevisions
                (RevisionId, ChangeSetId, DomainKind, EntityId,
                 GameAccountId, ChangeKind, BeforeVersion, AfterVersion,
                 SnapshotFormatVersion, BeforeSnapshotJson,
                 AfterSnapshotJson, CreatedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            revision.RevisionId.ToString("D"),
            changeSetId.ToString("D"),
            ManualAcquisitionEntityKind,
            identity.EntityId,
            portable.TargetAccountId.ToString("D"),
            (int)revision.ChangeKind,
            revision.BeforeVersion?.Value,
            revision.AfterVersion?.Value,
            DomainSnapshotFormatVersion,
            before is null ? null : SerializeDomainSnapshot(before),
            after is null ? null : SerializeDomainSnapshot(after),
            revision.CreatedAt.UtcDateTime.Ticks);
    }

    private static ArchiveDomainFactRow CreatePortableAcquisitionRow(
        ManualAcquisition acquisition,
        Guid archiveId,
        long version)
    {
        string payload = JsonSerializer.Serialize(
            ManualAcquisitionPayload.FromDomain(acquisition),
            DomainJsonOptions);
        return new ArchiveDomainFactRow
        {
            DomainKind = ManualAcquisitionEntityKind,
            EntityId = acquisition.AcquisitionId.ToString(),
            GameAccountId = acquisition.GameAccountId.ToString("D"),
            ArchiveId = archiveId.ToString("D"),
            TypeKey = acquisition.TypeId,
            SequenceUtcTicks = acquisition.OccurredAt.UtcStart?
                .UtcDateTime.Ticks,
            Version = version,
            ContentHash = ComputeDomainHash(payload),
            SnapshotFormatVersion = DomainSnapshotFormatVersion,
            SnapshotJson = payload,
        };
    }

    private static ManualAcquisition MapAcquisition(
        ManualAcquisition source,
        Guid targetAccountId) =>
        new(
            source.AcquisitionId,
            targetAccountId,
            source.Kind,
            source.TypeId,
            source.OccurredAt,
            source.Reason,
            source.ReasonDetail,
            source.Provenance);

    private static GameServerRegion ResolvePortableServerRegion(
        string server) => server switch
        {
            "cn_gf01" => GameServerRegion.ChinaOfficial,
            "cn_qd01" => GameServerRegion.ChinaBilibili,
            "os_usa" => GameServerRegion.America,
            "os_euro" => GameServerRegion.Europe,
            "os_asia" => GameServerRegion.Asia,
            "os_cht" => GameServerRegion.TaiwanHongKongMacao,
            _ => GameServerRegion.Unknown,
        };

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
        DomainProposal proposal,
        string? roleIdentityId = null) =>
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
                    roleIdentityId ?? ReadRoleIdentityId(
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

    private static ManualAcquisition?
        DeserializeAcquisitionFromDomainSnapshot(string? snapshotJson)
    {
        ArchiveDomainFactRow? row =
            DeserializeDomainSnapshot(snapshotJson);
        return row is null
            ? null
            : DeserializeManualAcquisition(row.SnapshotJson);
    }

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
        Guid? CleanupConfirmationId,
        PortableApplyContext? Portable = null)
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

    private sealed record PortableApplyContext(
        ArchiveDomainDatasetKind DatasetKind,
        string PackageFingerprint,
        Guid TargetArchiveId,
        DateTimeOffset ReceivedAt,
        PlayerArchive? ArchiveToCreate,
        IReadOnlyList<PortableAccountExpectation> Accounts,
        IReadOnlyList<PortableManualRevision> ManualRevisions,
        int ExpectedAddCount,
        int ExpectedSkipCount)
    {
        public PortableAccountExpectation? FindAccount(Guid accountId) =>
            Accounts.SingleOrDefault(item => item.Account.Id == accountId);

        public bool HasRevisionFor(string entityId) =>
            ManualRevisions.Any(item =>
                item.Revision.Before?.AcquisitionId.ToString() == entityId ||
                item.Revision.After?.AcquisitionId.ToString() == entityId);
    }

    private sealed record PortableAccountExpectation(
        GameAccount Account,
        bool Create);

    private sealed record PortableManualRevision(
        Guid TargetAccountId,
        PortableManualAcquisitionRevision Revision);

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
