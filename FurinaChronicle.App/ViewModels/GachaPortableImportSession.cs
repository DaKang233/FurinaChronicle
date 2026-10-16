// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.App.ViewModels;

public sealed class GachaPortableImportSession : IAsyncDisposable
{
    internal GachaPortableImportSession(
        IGachaPortableIndexedPackage package,
        GachaPortableImportPlan plan,
        DateTimeOffset receivedAt,
        string sourceSha256,
        long sourceLength)
    {
        Package = package;
        Plan = plan;
        ReceivedAt = receivedAt;
        SourceSha256 = sourceSha256;
        SourceLength = sourceLength;
        OperationId = OperationId.New();
        ReceiptBatchId = Guid.NewGuid();
    }

    public IGachaPortableIndexedPackage Package { get; }

    public GachaPortableImportPlan Plan { get; internal set; }

    public DateTimeOffset ReceivedAt { get; }

    public OperationId OperationId { get; }

    public Guid ReceiptBatchId { get; }

    public string SourceSha256 { get; }

    public long SourceLength { get; }

    public ValueTask DisposeAsync() => Package.DisposeAsync();
}

public sealed record GachaPortableExportArtifact(
    string TemporaryPath,
    GachaPortableWriteResult Result);
