// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.HistoryDomains;
using SQLite;

namespace FurinaChronicle.Infrastructure.Chronicle;

public sealed class SqliteChronicleQueryService(
    FurinaDatabase database,
    IGachaRecordRepository gachaRepository,
    IGameAccountRepository accountRepository,
    IArchiveDomainHistoryStore domainStore) : IChronicleQueryService
{
    private const int SourcePageSize = 500;
    private const int MaximumSourceFactsPerAccount = 250_000;
    private const int MaximumProjectedEntriesPerScope = 250_000;
    private const int MaximumGenerationsPerScope = 2;
    private static readonly TimeSpan GenerationLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChroniclePage> QueryAsync(
        ChronicleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await database.InitializeAsync(cancellationToken);
        string queryHash = HashQuery(query);
        CursorWire? cursor = null;
        if (query.Cursor is not null)
        {
            try
            {
                cursor = DecodeCursor(query.Cursor);
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                return Error(ChronicleQueryError.InvalidCursor, "The Chronicle cursor is malformed.");
            }
            if (cursor.Version != ChronicleVersions.Cursor)
            {
                return Error(ChronicleQueryError.CursorVersionMismatch, "The Chronicle cursor version is unsupported.");
            }
            if (!string.Equals(cursor.QueryHash, queryHash, StringComparison.Ordinal))
            {
                return Error(ChronicleQueryError.InvalidCursor, "The Chronicle cursor belongs to another scope or filter.");
            }
        }

        GenerationRow? generation = cursor is null
            ? await FindCurrentGenerationAsync(query.Scope, cancellationToken)
            : await FindGenerationAsync(cursor.GenerationId, cancellationToken);
        if (generation is null && cursor is null)
        {
            await RebuildAsync(query.Scope, EmptyChronicleItemCatalogSnapshot.Instance, cancellationToken);
            generation = await FindCurrentGenerationAsync(query.Scope, cancellationToken);
        }
        if (generation is null)
        {
            return Error(ChronicleQueryError.CursorExpired, "The Chronicle generation is no longer available.");
        }
        if (generation.IsInvalidated != 0)
        {
            return Error(ChronicleQueryError.CursorInvalidated, "The Chronicle generation was invalidated by a correction, delete, or undo.");
        }
        if (generation.ExpiresAtUtcTicks <= DateTimeOffset.UtcNow.UtcTicks)
        {
            return Error(ChronicleQueryError.CursorExpired, "The Chronicle generation has expired.");
        }
        ScopeStateRow state = await EnsureScopeStateAsync(query.Scope, cancellationToken);
        if (generation.DestructiveEpoch != state.DestructiveEpoch)
        {
            return Error(ChronicleQueryError.CursorInvalidated, "The Chronicle source facts changed destructively.");
        }

        (string where, List<object> args) = BuildWhere(query, generation.GenerationId, cursor);
        List<CacheEntryRow> rows = await database.Connection.QueryAsync<CacheEntryRow>(
            $"""
            SELECT GenerationId, EntryId, SortAtUtcTicks, IsUndated, PayloadJson
            FROM ChronicleEntries
            WHERE {where}
            ORDER BY IsUndated ASC, SortAtUtcTicks DESC, EntryId DESC
            LIMIT ?;
            """,
            [.. args, query.PageSize + 1]);
        bool hasMore = rows.Count > query.PageSize;
        CacheEntryRow[] pageRows = rows.Take(query.PageSize).ToArray();
        ChronicleEntry[] entries = pageRows.Select(row =>
            JsonSerializer.Deserialize<EntryWire>(row.PayloadJson, JsonOptions)!.ToDomain()).ToArray();
        string? next = hasMore && pageRows.Length > 0
            ? EncodeCursor(new CursorWire(
                ChronicleVersions.Cursor,
                generation.GenerationId,
                queryHash,
                pageRows[^1].IsUndated,
                pageRows[^1].SortAtUtcTicks,
                pageRows[^1].EntryId))
            : null;
        return new ChroniclePage(entries, next, generation.GenerationId);
    }

    public async Task RebuildAsync(
        ChronicleScope scope,
        IChronicleItemCatalogSnapshot itemCatalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(itemCatalog);
        scope.Validate();
        await database.InitializeAsync(cancellationToken);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            ScopeStateRow before = await EnsureScopeStateAsync(scope, cancellationToken);
            Guid[] accountIds = await ResolveAccountsAsync(scope, cancellationToken);
            var entries = new List<ChronicleEntry>();
            foreach (Guid accountId in accountIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var gacha = new List<GachaRecord>();
                for (int offset = 0;; offset += SourcePageSize)
                {
                    IReadOnlyList<GachaRecord> page = await gachaRepository.GetPageAsync(
                        accountId, offset, SourcePageSize, cancellationToken);
                    EnsureCapacity(gacha.Count, page.Count, "Gacha facts");
                    gacha.AddRange(page);
                    if (page.Count < SourcePageSize)
                    {
                        break;
                    }
                }
                IReadOnlyList<ManualAcquisitionState> manual =
                    await ReadManualAsync(accountId, cancellationToken);
                IReadOnlyList<ItemStateObservation> itemObservations =
                    await ReadItemsAsync(accountId, cancellationToken);
                IReadOnlyList<Core.Challenges.ChallengeObservation> challengeObservations =
                    await ReadChallengesAsync(accountId, cancellationToken);
                entries.AddRange(new GachaAcquisitionChronicleContributor().Project(
                    accountId, gacha, manual, itemCatalog, new ChronicleProjectionEvidence()));
                entries.AddRange(new InventoryChronicleContributor().Project(accountId, itemObservations));
                entries.AddRange(new ChallengeChronicleContributor().Project(accountId, challengeObservations));
                if (entries.Count > MaximumProjectedEntriesPerScope)
                {
                    throw new InvalidOperationException(
                        $"Chronicle projection exceeds the {MaximumProjectedEntriesPerScope} entry safety limit.");
                }
            }

            ScopeStateRow after = await EnsureScopeStateAsync(scope, cancellationToken);
            if (before.SourceEpoch != after.SourceEpoch ||
                before.DestructiveEpoch != after.DestructiveEpoch)
            {
                continue;
            }
            await PublishAsync(scope, after, itemCatalog.Version, entries, cancellationToken);
            return;
        }
        throw new InvalidOperationException("Chronicle source facts changed repeatedly while building a generation.");
    }

    public async Task ClearCacheAsync(
        ChronicleScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await database.InitializeAsync(cancellationToken);
        await database.Connection.ExecuteAsync(
            "DELETE FROM ChronicleGenerations WHERE ScopeKind = ? AND ScopeId = ?;",
            (int)scope.Kind, scope.Id.ToString("D"));
    }

    public async Task ExportMarkdownAsync(
        ChronicleQuery query,
        TextWriter destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        await destination.WriteLineAsync("# Furina Chronicle");
        await destination.WriteLineAsync();
        ChronicleQuery current = query with { Cursor = null };
        do
        {
            ChroniclePage page = await QueryAsync(current, cancellationToken);
            if (page.Error != ChronicleQueryError.None)
            {
                throw new InvalidOperationException(page.ErrorMessage);
            }
            foreach (ChronicleEntry entry in page.Entries)
            {
                string when = entry.Time.IsUndated
                    ? "Undated"
                    : entry.Time.SortAtUtc!.Value.ToString("O");
                await destination.WriteLineAsync(
                    $"- {when} | {entry.TitleKey} | {entry.BaseOrigin} / {entry.AnnotationOrigin} | {entry.Evidence.ReasonKey}");
            }
            current = current with { Cursor = page.NextCursor };
        }
        while (current.Cursor is not null);
        await destination.WriteLineAsync();
        await destination.WriteLineAsync($"Projection rule: {ChronicleVersions.Projection}");
    }

    private async Task PublishAsync(
        ChronicleScope scope,
        ScopeStateRow expected,
        string catalogVersion,
        IReadOnlyList<ChronicleEntry> entries,
        CancellationToken cancellationToken)
    {
        string generationId = Guid.NewGuid().ToString("D");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        EntryWire[] wires = entries.Select(EntryWire.FromDomain).ToArray();
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScopeStateRow actual = connection.Query<ScopeStateRow>(
                "SELECT * FROM ChronicleScopeStates WHERE ScopeKind = ? AND ScopeId = ?;",
                (int)scope.Kind, scope.Id.ToString("D")).Single();
            if (actual.SourceEpoch != expected.SourceEpoch ||
                actual.DestructiveEpoch != expected.DestructiveEpoch)
            {
                throw new InvalidOperationException("Chronicle source changed before generation publication.");
            }
            connection.Execute(
                """
                INSERT INTO ChronicleGenerations
                    (GenerationId, ScopeKind, ScopeId, SourceEpoch, DestructiveEpoch,
                     ProjectionVersion, ProfileVersion, CatalogVersion,
                     CreatedAtUtcTicks, ExpiresAtUtcTicks, IsInvalidated)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0);
                """,
                generationId, (int)scope.Kind, scope.Id.ToString("D"),
                expected.SourceEpoch, expected.DestructiveEpoch,
                ChronicleVersions.Projection, ChronicleVersions.Profile,
                catalogVersion, now.UtcTicks, now.Add(GenerationLifetime).UtcTicks);
            foreach (EntryWire wire in wires)
            {
                connection.Execute(
                    """
                    INSERT INTO ChronicleEntries
                        (GenerationId, EntryId, SortAtUtcTicks, IsUndated,
                         OccurredStartUtcTicks, OccurredEndUtcTicks, Domain, Kind,
                         Timelines, MinimumGranularity, BaseOrigin, RankType, ItemId, PayloadJson)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    generationId, wire.EntryId,
                    wire.SortAt?.ToUniversalTime().Ticks,
                    wire.SortAt is null ? 1 : 0,
                    wire.Occurred?.Start?.ToUniversalTime().Ticks,
                    wire.Occurred?.End?.ToUniversalTime().Ticks,
                    wire.Domain, wire.Kind, wire.Timelines, wire.MinimumGranularity,
                    wire.BaseOrigin, wire.RankType, wire.ItemId,
                    JsonSerializer.Serialize(wire, JsonOptions));
            }
            List<string> stale = connection.Query<GenerationIdRow>(
                    """
                    SELECT GenerationId FROM ChronicleGenerations
                    WHERE ScopeKind = ? AND ScopeId = ?
                    ORDER BY CreatedAtUtcTicks DESC
                    LIMIT -1 OFFSET ?;
                    """,
                    (int)scope.Kind, scope.Id.ToString("D"), MaximumGenerationsPerScope)
                .Select(item => item.GenerationId).ToList();
            foreach (string id in stale)
            {
                connection.Execute("DELETE FROM ChronicleGenerations WHERE GenerationId = ?;", id);
            }
        });
    }

    private async Task<Guid[]> ResolveAccountsAsync(
        ChronicleScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.Kind == ChronicleScopeKind.Account)
        {
            return [scope.Id];
        }
        var accounts = await accountRepository.GetByArchiveIdAsync(scope.Id, cancellationToken);
        IEnumerable<Guid> ids = accounts.Select(item => item.Id);
        if (scope.SelectedAccountIds is { Count: > 0 })
        {
            ids = ids.Where(scope.SelectedAccountIds.Contains);
        }
        return ids.Order().ToArray();
    }

    private async Task<IReadOnlyList<ItemStateObservation>> ReadItemsAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<ItemStateObservation>();
        for (int offset = 0;; offset += SourcePageSize)
        {
            ArchiveDomainPage<ItemStateObservation> page =
                await domainStore.GetItemObservationPageAsync(
                    new ItemObservationPageQuery(accountId, Offset: offset,
                        Limit: SourcePageSize), cancellationToken);
            result.AddRange(page.Items);
            EnsureCapacity(result.Count - page.Items.Count, page.Items.Count,
                "item observations");
            if (result.Count >= page.TotalCount) return result;
        }
    }

    private async Task<IReadOnlyList<ManualAcquisitionState>> ReadManualAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = new List<ManualAcquisitionState>();
        for (int offset = 0;; offset += SourcePageSize)
        {
            ArchiveDomainPage<ManualAcquisitionState> page =
                await domainStore.GetManualAcquisitionPageAsync(
                    new ManualAcquisitionPageQuery(accountId, Offset: offset,
                        Limit: SourcePageSize), cancellationToken);
            result.AddRange(page.Items);
            EnsureCapacity(result.Count - page.Items.Count, page.Items.Count,
                "manual acquisitions");
            if (result.Count >= page.TotalCount) return result;
        }
    }

    private async Task<IReadOnlyList<Core.Challenges.ChallengeObservation>>
        ReadChallengesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var result = new List<Core.Challenges.ChallengeObservation>();
        for (int offset = 0;; offset += SourcePageSize)
        {
            ArchiveDomainPage<Core.Challenges.ChallengeObservation> page =
                await domainStore.GetChallengeObservationPageAsync(
                    new ChallengeObservationPageQuery(accountId, Offset: offset,
                        Limit: SourcePageSize), cancellationToken);
            result.AddRange(page.Items);
            EnsureCapacity(result.Count - page.Items.Count, page.Items.Count,
                "challenge observations");
            if (result.Count >= page.TotalCount) return result;
        }
    }

    private static void EnsureCapacity(int current, int added, string source)
    {
        if ((long)current + added > MaximumSourceFactsPerAccount)
        {
            throw new InvalidOperationException(
                $"{source} exceed the {MaximumSourceFactsPerAccount} per-account Chronicle safety limit.");
        }
    }

    private async Task<ScopeStateRow> EnsureScopeStateAsync(
        ChronicleScope scope,
        CancellationToken cancellationToken)
    {
        await database.Connection.ExecuteAsync(
            "INSERT OR IGNORE INTO ChronicleScopeStates (ScopeKind, ScopeId, SourceEpoch, DestructiveEpoch) VALUES (?, ?, 0, 0);",
            (int)scope.Kind, scope.Id.ToString("D"));
        cancellationToken.ThrowIfCancellationRequested();
        return (await database.Connection.QueryAsync<ScopeStateRow>(
            "SELECT * FROM ChronicleScopeStates WHERE ScopeKind = ? AND ScopeId = ?;",
            (int)scope.Kind, scope.Id.ToString("D"))).Single();
    }

    private async Task<GenerationRow?> FindCurrentGenerationAsync(
        ChronicleScope scope,
        CancellationToken cancellationToken)
    {
        ScopeStateRow state = await EnsureScopeStateAsync(scope, cancellationToken);
        return (await database.Connection.QueryAsync<GenerationRow>(
            """
            SELECT * FROM ChronicleGenerations
            WHERE ScopeKind = ? AND ScopeId = ? AND SourceEpoch = ?
              AND DestructiveEpoch = ? AND IsInvalidated = 0
              AND ProjectionVersion = ? AND ProfileVersion = ?
            ORDER BY CreatedAtUtcTicks DESC LIMIT 1;
            """,
            (int)scope.Kind, scope.Id.ToString("D"), state.SourceEpoch,
            state.DestructiveEpoch, ChronicleVersions.Projection,
            ChronicleVersions.Profile)).SingleOrDefault();
    }

    private async Task<GenerationRow?> FindGenerationAsync(
        string id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await database.Connection.QueryAsync<GenerationRow>(
            "SELECT * FROM ChronicleGenerations WHERE GenerationId = ?;", id))
            .SingleOrDefault();
    }

    private static (string Where, List<object> Arguments) BuildWhere(
        ChronicleQuery query,
        string generationId,
        CursorWire? cursor)
    {
        var clauses = new List<string>
        {
            "GenerationId = ?",
            "(Timelines & ?) != 0",
            "MinimumGranularity <= ?",
        };
        var args = new List<object>
        {
            generationId, (int)query.Profile.Timeline, (int)query.Profile.Granularity,
        };
        if (!query.Profile.IncludeStateChanges)
        {
            clauses.Add("Kind NOT IN (?, ?)");
            args.Add((int)ChronicleEntryKind.StateObservation);
            args.Add((int)ChronicleEntryKind.StateChange);
        }
        AddSet(query.Filter.Domains, "Domain", value => (int)value);
        AddSet(query.Filter.Kinds, "Kind", value => (int)value);
        AddSet(query.Filter.Origins, "BaseOrigin", value => (int)value);
        AddSet(query.Filter.Ranks, "RankType", value => value);
        AddSet(query.Filter.ItemIds, "ItemId", value => value);
        if (query.Filter.From is not null || query.Filter.To is not null)
        {
            long from = query.Filter.From?.ToUniversalTime().Ticks ?? long.MinValue;
            long to = query.Filter.To?.ToUniversalTime().Ticks ?? long.MaxValue;
            clauses.Add("((OccurredStartUtcTicks IS NOT NULL AND OccurredStartUtcTicks < ? AND COALESCE(OccurredEndUtcTicks, OccurredStartUtcTicks + 1) > ?) OR (OccurredStartUtcTicks IS NULL AND SortAtUtcTicks >= ? AND SortAtUtcTicks < ?))");
            args.Add(to); args.Add(from); args.Add(from); args.Add(to);
        }
        if (!query.Filter.IncludeUndated)
        {
            clauses.Add("IsUndated = 0");
        }
        if (cursor is not null)
        {
            if (cursor.IsUndated == 0)
            {
                clauses.Add("(IsUndated > 0 OR (IsUndated = 0 AND (SortAtUtcTicks < ? OR (SortAtUtcTicks = ? AND EntryId < ?))))");
                args.Add(cursor.SortAtUtcTicks!); args.Add(cursor.SortAtUtcTicks!); args.Add(cursor.EntryId);
            }
            else
            {
                clauses.Add("IsUndated = 1 AND EntryId < ?");
                args.Add(cursor.EntryId);
            }
        }
        return (string.Join(" AND ", clauses), args);

        void AddSet<T>(IReadOnlySet<T>? values, string column, Func<T, object> map)
        {
            if (values is not { Count: > 0 }) return;
            clauses.Add($"{column} IN ({string.Join(",", values.Select(_ => "?"))})");
            args.AddRange(values.OrderBy(value => value).Select(map));
        }
    }

    private static string HashQuery(ChronicleQuery query)
    {
        string canonical = string.Join("|",
            (int)query.Scope.Kind, query.Scope.Id.ToString("D"),
            string.Join(",", query.Scope.SelectedAccountIds?.Order() ??
                Enumerable.Empty<Guid>()),
            (int)query.Profile.Timeline, (int)query.Profile.Granularity,
            query.Profile.IncludeStateChanges,
            query.Filter.From?.ToUniversalTime().Ticks,
            query.Filter.To?.ToUniversalTime().Ticks,
            query.Filter.IncludeUndated,
            Set(query.Filter.Domains), Set(query.Filter.Kinds),
            Set(query.Filter.Origins), Set(query.Filter.Ranks),
            Set(query.Filter.ItemIds), ChronicleVersions.Projection,
            ChronicleVersions.Profile);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        static string Set<T>(IReadOnlySet<T>? values) =>
            values is null ? string.Empty : string.Join(",", values.OrderBy(value => value));
    }

    private static string EncodeCursor(CursorWire cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, JsonOptions))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static CursorWire DecodeCursor(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return JsonSerializer.Deserialize<CursorWire>(Convert.FromBase64String(padded), JsonOptions)
            ?? throw new JsonException("Cursor payload is empty.");
    }

    private static ChroniclePage Error(ChronicleQueryError error, string message) =>
        new([], null, string.Empty, error, message);

    private sealed class ScopeStateRow
    {
        public int ScopeKind { get; set; }
        public string ScopeId { get; set; } = string.Empty;
        public long SourceEpoch { get; set; }
        public long DestructiveEpoch { get; set; }
    }

    private sealed class GenerationRow
    {
        public string GenerationId { get; set; } = string.Empty;
        public long SourceEpoch { get; set; }
        public long DestructiveEpoch { get; set; }
        public long ExpiresAtUtcTicks { get; set; }
        public int IsInvalidated { get; set; }
    }

    private sealed class GenerationIdRow { public string GenerationId { get; set; } = string.Empty; }
    private sealed class CacheEntryRow
    {
        public string GenerationId { get; set; } = string.Empty;
        public string EntryId { get; set; } = string.Empty;
        public long? SortAtUtcTicks { get; set; }
        public int IsUndated { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
    }

    private sealed record CursorWire(
        int Version,
        string GenerationId,
        string QueryHash,
        int IsUndated,
        long? SortAtUtcTicks,
        string EntryId);

    private sealed record OccurrenceWire(
        int Kind,
        DateTimeOffset? Start,
        DateTimeOffset? End,
        int? Precision)
    {
        public static OccurrenceWire? FromDomain(OccurrenceTime? value) => value is null
            ? null
            : new((int)value.Kind, value.Start, value.End, (int?)value.Precision);

        public OccurrenceTime? ToDomain() => (OccurrenceTimeKind)Kind switch
        {
            OccurrenceTimeKind.Instant => OccurrenceTime.At(Start!.Value, (TimePrecision)Precision!.Value),
            OccurrenceTimeKind.Interval => OccurrenceTime.ForInterval(Start!.Value, End!.Value, (TimePrecision)Precision!.Value),
            OccurrenceTimeKind.UncertainInterval => OccurrenceTime.Within(Start!.Value, End!.Value, (TimePrecision)Precision!.Value),
            _ => null,
        };
    }

    private sealed record EntryWire(
        string EntryId, string? PortableEntityKey, int ProjectionVersion,
        Guid GameAccountId, int Domain, int Kind, int Timelines,
        int MinimumGranularity, string? Milestone, OccurrenceWire? Occurred,
        DateTimeOffset? ObservedAt, DateTimeOffset? FetchedAt, DateTimeOffset? SortAt,
        int BaseOrigin, int AnnotationOrigin, int Confidence, int Completeness,
        string SourceEntityType, string SourceEntityId, string TitleKey,
        Dictionary<string, string> SummaryArguments, int Importance,
        int EvidenceStatus, int EvidenceScope, string EvidenceReason,
        string[] EvidenceInputs, string? ItemId, int? RankType,
        string? CorrelationId, int? LocalRecordedPulls, int? ExactPity)
    {
        public static EntryWire FromDomain(ChronicleEntry value) => new(
            value.EntryId.Value, value.PortableEntityKey, value.ProjectionVersion,
            value.GameAccountId, (int)value.Domain, (int)value.Kind,
            (int)value.Timelines, (int)value.MinimumGranularity, value.Milestone,
            OccurrenceWire.FromDomain(value.Time.Occurred), value.Time.ObservedAt,
            value.Time.FetchedAt, value.Time.SortAtUtc, (int)value.BaseOrigin,
            (int)value.AnnotationOrigin, (int)value.Confidence,
            (int)value.Completeness, value.SourceEntityType, value.SourceEntityId,
            value.TitleKey, new(value.SummaryArguments, StringComparer.Ordinal),
            (int)value.Importance, (int)value.Evidence.Status,
            (int)value.Evidence.Scope, value.Evidence.ReasonKey,
            value.Evidence.InputReferences.ToArray(), value.ItemId, value.RankType,
            value.CorrelationId, value.LocalRecordedPulls, value.ExactPity);

        public ChronicleEntry ToDomain() => new(
            new ChronicleEntryId(EntryId), PortableEntityKey, ProjectionVersion,
            GameAccountId, (ChronicleDomain)Domain, (ChronicleEntryKind)Kind,
            (ChronicleTimeline)Timelines, (ChronicleGranularity)MinimumGranularity,
            Milestone, new ChronicleTime(Occurred?.ToDomain(), ObservedAt, FetchedAt),
            (DataOrigin)BaseOrigin, (DataOrigin)AnnotationOrigin,
            (DataConfidence)Confidence, (DataCompleteness)Completeness,
            SourceEntityType, SourceEntityId, TitleKey, SummaryArguments,
            (ChronicleImportance)Importance,
            new ChronicleEvidence((ChronicleEvidenceStatus)EvidenceStatus,
                (ChronicleEvidenceScope)EvidenceScope, EvidenceReason, EvidenceInputs),
            ItemId, RankType, CorrelationId, LocalRecordedPulls, ExactPity);
    }
}
