// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public interface IArchiveExportSnapshot : IAsyncDisposable
{
    DateTimeOffset GeneratedAt { get; }

    ArchiveExportScope Scope { get; }

    ArchiveCatalog Catalog { get; }

    ArchivePortableSettings Settings { get; }

    IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ManualAcquisitionRevisionItem>
        ReadManualAcquisitionRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);
}

public interface IArchiveExportSnapshotFactory
{
    Task<IArchiveExportSnapshot> OpenAsync(
        ArchiveExportScope scope,
        CancellationToken cancellationToken = default);
}

public interface IArchivePackageWriter
{
    Task<ArchiveWriteResult> WriteAsync(
        Stream destination,
        IArchiveExportSnapshot snapshot,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default);
}

public interface IArchivePackageReader
{
    Task<ArchivePackageSummary> ReadAndValidateAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface IArchiveLogicalExporter
{
    Task<ArchiveWriteResult> ExportAsync(
        Stream destination,
        ArchiveExportScope scope,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default);
}

public interface IArchiveRestorePlanner
{
}

public interface IArchiveAtomicRestorer
{
}
