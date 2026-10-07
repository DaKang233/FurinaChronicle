// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Writing;

namespace FurinaChronicle.Services.Gacha.Refreshing;

public sealed class RefreshGachaRecords(
    IGachaRecordRepository recordRepository,
    ICommitGachaRecords commitGachaRecords,
    IPassportAccountStore passportAccountStore,
    ISTokenGachaUrlProvider sTokenUrlProvider,
    IWindowsGachaCacheUrlProvider windowsCacheUrlProvider,
    IGachaLogClient gachaLogClient,
    IGachaItemMetadataProvider metadataProvider)
{
    private static readonly string[] GachaTypes =
        ["301", "302", "500", "100", "200"];

    public async Task<GachaRefreshResult> ExecuteAsync(
        GachaRefreshRequest request,
        IProgress<GachaRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GameAccount.Id == Guid.Empty)
        {
            throw new ArgumentException("Game account ID is required.", nameof(request));
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Uri sourceUrl = await ResolveSourceUrlAsync(request, cancellationToken);
        AcquisitionBatchId batchId = AcquisitionBatchId.New();
        HashSet<string> localIds = request.Mode == GachaRefreshMode.Incremental
            ? await LoadExistingIdsAsync(request.GameAccount.Id, cancellationToken)
            : new HashSet<string>(StringComparer.Ordinal);
        var collected = new List<GachaRecord>();
        var collectedIds = new HashSet<string>(StringComparer.Ordinal);
        var metadataByName =
            new Dictionary<string, GachaItemMetadata?>(StringComparer.Ordinal);
        int pageCount = 0;
        int boundaryCount = 0;

        foreach (string gachaType in GachaTypes)
        {
            string? endId = null;
            int page = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GachaRemotePage remotePage = await gachaLogClient.GetPageAsync(
                    sourceUrl,
                    gachaType,
                    endId,
                    cancellationToken);
                DateTimeOffset fetchedAt = DateTimeOffset.UtcNow;
                var pageProvenance = new RecordProvenance(
                    DataOrigin.OfficialApi,
                    new RecordTimestamps(FetchedAt: fetchedAt),
                    acquisitionBatchId: batchId);
                pageCount++;

                bool reachedBoundary = false;
                foreach (GachaRemoteRecord remote in remotePage.Records)
                {
                    if (!string.IsNullOrWhiteSpace(remote.Uid) &&
                        !string.Equals(
                            remote.Uid,
                            request.GameAccount.Uid,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Gacha response UID {remote.Uid} does not match target UID {request.GameAccount.Uid}.");
                    }

                    if (request.Mode == GachaRefreshMode.Incremental &&
                        localIds.Contains(remote.ExternalRecordId))
                    {
                        reachedBoundary = true;
                        boundaryCount++;
                        break;
                    }

                    if (collectedIds.Add(remote.ExternalRecordId))
                    {
                        GachaRemoteRecord completed =
                            await CompleteMetadataAsync(
                                remote,
                                metadataByName,
                                cancellationToken);
                        collected.Add(completed.ToDomain(
                            request.GameAccount.Id,
                            pageProvenance));
                    }
                }

                progress?.Report(new GachaRefreshProgress(
                    gachaType,
                    page,
                    collected.Count));

                if (reachedBoundary || remotePage.Records.Count == 0 ||
                    string.IsNullOrWhiteSpace(remotePage.NextEndId) ||
                    string.Equals(endId, remotePage.NextEndId, StringComparison.Ordinal))
                {
                    break;
                }

                endId = remotePage.NextEndId;
                page++;
            }
        }

        CommitGachaRecordsResult saveResult =
            await commitGachaRecords.ExecuteAsync(
                new CommitGachaRecordsRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    request.Mode == GachaRefreshMode.Full
                        ? "Full Gacha refresh"
                        : "Incremental Gacha refresh",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    collected,
                    request.Mode == GachaRefreshMode.Full
                        ? GachaRecordConflictPolicy.ReplaceExisting
                        : GachaRecordConflictPolicy.PreserveExisting,
                    SuppressTombstonesAndContinue: true),
                cancellationToken);
        if (saveResult.Status is ChangeExecutionStatus.Conflict or
            ChangeExecutionStatus.NeedsConfirmation)
        {
            throw new InvalidOperationException(
                saveResult.ConflictReason ??
                "The Gacha refresh could not be committed safely.");
        }
        return new GachaRefreshResult(
            collected.Count,
            saveResult.InsertedCount,
            saveResult.UpdatedCount,
            saveResult.DuplicateCount,
            pageCount,
            boundaryCount,
            saveResult.SuppressedCount);
    }

    public async Task<GachaRefreshIdentity> DiscoverIdentityAsync(
        GachaRefreshDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri sourceUrl = request.Source switch
        {
            GachaRefreshSource.ManualUrl =>
                GachaRefreshUrl.Parse(request.ManualUrl ?? string.Empty),
            GachaRefreshSource.WindowsWebCache =>
                await DiscoverWindowsCacheUrlAsync(request, cancellationToken),
            GachaRefreshSource.SToken => throw new ArgumentException(
                "SToken refresh identity must come from the selected game role.",
                nameof(request)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Source,
                "Unsupported gacha refresh source.")
        };

        foreach (string gachaType in GachaTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaRemotePage page = await gachaLogClient.GetPageAsync(
                sourceUrl,
                gachaType,
                endId: null,
                cancellationToken);
            string[] uids = page.Records
                .Select(record => record.Uid)
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (uids.Length > 1)
            {
                throw new InvalidDataException(
                    "Gacha response contains records from more than one UID.");
            }

            if (uids.Length == 1)
            {
                GameServerRegion region = GameServerRegionResolver.Resolve(uids[0]);
                if (region == GameServerRegion.Unknown)
                {
                    throw new InvalidDataException(
                        $"Unable to determine server region for UID {uids[0]}.");
                }

                return new GachaRefreshIdentity(uids[0], region);
            }
        }

        throw new InvalidDataException(
            "The gacha URL returned no records, so its game UID could not be determined.");
    }

    private async Task<Uri> DiscoverWindowsCacheUrlAsync(
        GachaRefreshDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        if (!windowsCacheUrlProvider.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Web-cache gacha refresh is available only on Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameInstallationPath);
        return await windowsCacheUrlProvider.FindAsync(
            request.GameInstallationPath,
            request.ServerRegionHint,
            cancellationToken);
    }

    private async Task<Uri> ResolveSourceUrlAsync(
        GachaRefreshRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Source)
        {
            case GachaRefreshSource.SToken:
                if (request.PassportAccountId is not Guid passportAccountId ||
                    passportAccountId == Guid.Empty)
                {
                    throw new ArgumentException(
                        "SToken refresh requires a passport account ID.",
                        nameof(request));
                }

                PassportAccount passportAccount =
                    await passportAccountStore.GetByIdAsync(
                        passportAccountId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException(
                        "The passport account does not exist.");
                if (passportAccount.Credentials.SToken is null ||
                    passportAccount.Mid is null)
                {
                    throw new InvalidOperationException(
                        "The passport account does not contain SToken and mid.");
                }

                return await sTokenUrlProvider.CreateAsync(
                    passportAccount,
                    request.GameAccount,
                    cancellationToken);

            case GachaRefreshSource.WindowsWebCache:
                if (!windowsCacheUrlProvider.IsSupported)
                {
                    throw new PlatformNotSupportedException(
                        "Web-cache gacha refresh is available only on Windows.");
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(
                    request.GameInstallationPath);
                return await windowsCacheUrlProvider.FindAsync(
                    request.GameInstallationPath,
                    request.GameAccount.ServerRegion,
                    cancellationToken);

            case GachaRefreshSource.ManualUrl:
                return GachaRefreshUrl.Parse(request.ManualUrl ?? string.Empty);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Source,
                    "Unsupported gacha refresh source.");
        }
    }

    private async Task<HashSet<string>> LoadExistingIdsAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken)
    {
        int count = await recordRepository.CountAsync(
            gameAccountId,
            cancellationToken);
        if (count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        IReadOnlyList<GachaRecord> records = await recordRepository.GetPageAsync(
            gameAccountId,
            offset: 0,
            count,
            cancellationToken);
        return records
            .Select(record => record.ExternalRecordId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<GachaRemoteRecord> CompleteMetadataAsync(
        GachaRemoteRecord record,
        IDictionary<string, GachaItemMetadata?> metadataByName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(record.ItemId) ||
            string.IsNullOrWhiteSpace(record.ItemName))
        {
            return record;
        }

        string itemName = record.ItemName.Trim();
        if (!metadataByName.TryGetValue(
            itemName,
            out GachaItemMetadata? metadata))
        {
            metadata = await metadataProvider.FindByNameAsync(
                GachaGame.GenshinImpact,
                itemName,
                cancellationToken);
            metadataByName.Add(itemName, metadata);
        }

        return metadata is null
            ? record
            : record with
            {
                ItemId = metadata.ItemId,
                ItemType = record.ItemType ?? metadata.ItemType,
                RankType = record.RankType ?? metadata.RankType
            };
    }
}
