// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.App.ViewModels;

public sealed class GachaPortableImportSession : IAsyncDisposable
{
    private readonly IGachaPortableStagedInput stagedInput;

    internal GachaPortableImportSession(
        IGachaPortableStagedInput stagedInput,
        GachaPortablePackage package,
        GachaPortableImportPlan plan,
        DateTimeOffset receivedAt)
    {
        this.stagedInput = stagedInput;
        Package = package;
        Plan = plan;
        ReceivedAt = receivedAt;
        OperationId = OperationId.New();
        ReceiptBatchId = Guid.NewGuid();
    }

    public GachaPortablePackage Package { get; }

    public GachaPortableImportPlan Plan { get; internal set; }

    public DateTimeOffset ReceivedAt { get; }

    public OperationId OperationId { get; }

    public Guid ReceiptBatchId { get; }

    public string SourceSha256 => stagedInput.Sha256;

    public long SourceLength => stagedInput.Length;

    public ValueTask DisposeAsync() => stagedInput.DisposeAsync();
}

public sealed record GachaPortableExportArtifact(
    string TemporaryPath,
    GachaPortableWriteResult Result);
