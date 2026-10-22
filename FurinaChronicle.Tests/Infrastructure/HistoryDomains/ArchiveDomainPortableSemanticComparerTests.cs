// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class ArchiveDomainPortableSemanticComparerTests
{
    [Fact]
    public void Compare_AllowsOnlyReceiverLocalContextToChange()
    {
        ArchiveDomainPortablePackage source = Package();
        ArchiveDomainPortablePackage target = RemapLocalContext(source);

        ArchiveDomainPortableSemanticComparison comparison =
            ArchiveDomainPortableSemanticComparer.Compare(source, target);

        Assert.True(comparison.Equivalent);
        Assert.Equal(comparison.ExpectedDigest, comparison.ActualDigest);
        Assert.Empty(comparison.Differences);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("account")]
    public void Compare_DetectsRequiredPortableSemanticsLoss(string mutation)
    {
        ArchiveDomainPortablePackage source = Package();
        ArchiveDomainPortablePackage target = RemapLocalContext(source);
        target = mutation switch
        {
            "time" => MutateObservationTime(target),
            "source" => MutateObservationSource(target),
            "revision" => RemoveRevision(target),
            "account" => SwapAccountFacts(target),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        ArchiveDomainPortableSemanticComparison comparison =
            ArchiveDomainPortableSemanticComparer.Compare(source, target);

        Assert.False(comparison.Equivalent);
        Assert.NotEqual(comparison.ExpectedDigest, comparison.ActualDigest);
        Assert.NotEmpty(comparison.Differences);
    }

    private static ArchiveDomainPortablePackage Package()
    {
        Guid firstAccountId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid secondAccountId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        ArchiveDomainPortableAccount first = Account(
            firstAccountId,
            "800000001",
            "CHAR_10000089",
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        ArchiveDomainPortableAccount second = Account(
            secondAccountId,
            "700000002",
            "WEAPON_15514",
            Guid.Parse("20000000-0000-0000-0000-000000000002"));
        return new ArchiveDomainPortablePackage(
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            1,
            0,
            "source-fingerprint",
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "来源档案",
            TestTime(),
            [first, second]);
    }

    private static ArchiveDomainPortableAccount Account(
        Guid accountId,
        string uid,
        string typeId,
        Guid stableId)
    {
        GameRoleNaturalIdentity natural = GenshinGameRoleIdentity.Create(
            uid,
            uid.StartsWith('8')
                ? GameServerRegion.Asia
                : GameServerRegion.Europe);
        var identity = new GameRoleIdentity(
            GameRoleIdentityId.FromNaturalIdentity(natural),
            natural);
        InventoryItemKind kind = typeId.StartsWith("CHAR_", StringComparison.Ordinal)
            ? InventoryItemKind.Character
            : InventoryItemKind.Weapon;
        var provenance = new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(
                ObservedAt: TestTime(),
                FetchedAt: TestTime().AddMinutes(1)),
            new DataSourceReference(
                "miyoushe",
                sourceRecordId: $"record-{uid}",
                sourceSnapshotId: $"snapshot-{uid}"),
            new AcquisitionBatchId(stableId));
        var observation = new ItemStateObservation(
            new ItemObservationId(stableId),
            accountId,
            kind,
            DataCompleteness.Partial,
            [new InventoryStateEntry(
                kind,
                typeId,
                instanceId: null,
                quantity: 1,
                level: null,
                progression: null,
                InventoryFieldCoverage.Quantity,
                DataCompleteness.Partial)],
            provenance);
        var acquisition = new ManualAcquisition(
            new ManualAcquisitionId(stableId),
            accountId,
            kind,
            typeId,
            OccurrenceTime.Within(
                TestTime().AddDays(-7),
                TestTime(),
                TimePrecision.Week),
            ManualAcquisitionReason.EventSelection,
            "合成验收事件",
            new RecordProvenance(DataOrigin.UserEntered));
        var revision = new PortableManualAcquisitionRevision(
            stableId,
            EntityChangeKind.Insert,
            null,
            new FactVersion(1),
            null,
            acquisition,
            TestTime().AddMinutes(2));
        return new ArchiveDomainPortableAccount(
            accountId,
            identity,
            [observation],
            [acquisition],
            [revision],
            []);
    }

    private static ArchiveDomainPortablePackage RemapLocalContext(
        ArchiveDomainPortablePackage source)
    {
        ArchiveDomainPortableAccount[] accounts = source.Accounts
            .Select((account, index) => RemapAccount(
                account,
                Guid.Parse($"40000000-0000-0000-0000-{index + 1:D12}")))
            .ToArray();
        return source with
        {
            PackageFingerprint = "target-fingerprint",
            SourceArchiveId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            SourceArchiveName = "目标端档案名称",
            ExportedAt = TestTime().AddDays(1),
            Accounts = accounts
        };
    }

    private static ArchiveDomainPortableAccount RemapAccount(
        ArchiveDomainPortableAccount account,
        Guid targetAccountId) => account with
    {
        SourceAccountId = targetAccountId,
        ItemObservations = account.ItemObservations.Select(observation =>
            new ItemStateObservation(
                observation.ObservationId,
                targetAccountId,
                observation.Kind,
                observation.CollectionCompleteness,
                observation.Entries,
                observation.Provenance)).ToArray(),
        ManualAcquisitions = account.ManualAcquisitions.Select(acquisition =>
            RemapAcquisition(acquisition, targetAccountId)).ToArray(),
        ManualRevisions = account.ManualRevisions.Select(revision =>
            revision with
            {
                Before = revision.Before is null
                    ? null
                    : RemapAcquisition(revision.Before, targetAccountId),
                After = revision.After is null
                    ? null
                    : RemapAcquisition(revision.After, targetAccountId)
            }).ToArray()
    };

    private static ManualAcquisition RemapAcquisition(
        ManualAcquisition acquisition,
        Guid accountId) => new(
        acquisition.AcquisitionId,
        accountId,
        acquisition.Kind,
        acquisition.TypeId,
        acquisition.OccurredAt,
        acquisition.Reason,
        acquisition.ReasonDetail,
        acquisition.Provenance);

    private static ArchiveDomainPortablePackage MutateObservationTime(
        ArchiveDomainPortablePackage package) => MutateFirstObservation(
        package,
        observation => new ItemStateObservation(
            observation.ObservationId,
            observation.GameAccountId,
            observation.Kind,
            observation.CollectionCompleteness,
            observation.Entries,
            new RecordProvenance(
                observation.Provenance.Origin,
                new RecordTimestamps(
                    ObservedAt: TestTime().AddMinutes(5),
                    FetchedAt: observation.Provenance.Timestamps.FetchedAt),
                observation.Provenance.Source,
                observation.Provenance.AcquisitionBatchId)));

    private static ArchiveDomainPortablePackage MutateObservationSource(
        ArchiveDomainPortablePackage package) => MutateFirstObservation(
        package,
        observation => new ItemStateObservation(
            observation.ObservationId,
            observation.GameAccountId,
            observation.Kind,
            observation.CollectionCompleteness,
            observation.Entries,
            new RecordProvenance(
                DataOrigin.StandardImport,
                observation.Provenance.Timestamps,
                observation.Provenance.Source,
                observation.Provenance.AcquisitionBatchId)));

    private static ArchiveDomainPortablePackage RemoveRevision(
        ArchiveDomainPortablePackage package) => package with
    {
        Accounts = package.Accounts.Select((account, index) => index == 0
            ? account with { ManualRevisions = [] }
            : account).ToArray()
    };

    private static ArchiveDomainPortablePackage SwapAccountFacts(
        ArchiveDomainPortablePackage package)
    {
        ArchiveDomainPortableAccount first = package.Accounts[0];
        ArchiveDomainPortableAccount second = package.Accounts[1];
        return package with
        {
            Accounts =
            [
                first with
                {
                    ItemObservations = second.ItemObservations,
                    ManualAcquisitions = second.ManualAcquisitions,
                    ManualRevisions = second.ManualRevisions
                },
                second with
                {
                    ItemObservations = first.ItemObservations,
                    ManualAcquisitions = first.ManualAcquisitions,
                    ManualRevisions = first.ManualRevisions
                }
            ]
        };
    }

    private static ArchiveDomainPortablePackage MutateFirstObservation(
        ArchiveDomainPortablePackage package,
        Func<ItemStateObservation, ItemStateObservation> mutate) => package with
    {
        Accounts = package.Accounts.Select((account, index) => index == 0
            ? account with
            {
                ItemObservations = account.ItemObservations
                    .Select((observation, observationIndex) =>
                        observationIndex == 0 ? mutate(observation) : observation)
                    .ToArray()
            }
            : account).ToArray()
    };

    private static DateTimeOffset TestTime() =>
        new(2026, 10, 20, 12, 34, 56, TimeSpan.FromHours(8));
}
