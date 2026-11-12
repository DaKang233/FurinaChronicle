// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Tests.Presentation;

// D6 regression: package/stager validation failures must map to explicit refusal text rather than
// the generic "operation did not complete" fallback. No WinUI is involved.
public sealed class ArchiveTransferErrorMappingTests
{
    [Theory]
    [MemberData(nameof(Errors))]
    public async Task InvalidPackageFailuresUseExplicitSafeMessages(
        Exception exception,
        string expectedFragment)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTransferErrors",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "invalid.fca");
        await File.WriteAllTextAsync(file, "not an archive");
        try
        {
            var state = new ArchiveTransferState(
                new ThrowingStageSource(exception),
                new TestWorkspace(root),
                TimeProvider.System);

            ArchiveTransferActionResult result = await state.OpenPackageAsync(file);

            Assert.False(result.Succeeded);
            Assert.Contains(expectedFragment, result.Message);
            Assert.Contains("未写入任何数据", result.Message);
            Assert.False(state.Snapshot.HasPackage);
            Assert.NotNull(state.Snapshot.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static TheoryData<Exception, string> Errors => new()
    {
        {
            new ArchivePackageException(
                ArchivePackageErrorCode.InvalidPackage, "not a package"),
            "不是有效的完整逻辑档案"
        },
        {
            new ArchivePackageException(
                ArchivePackageErrorCode.HashMismatch, "hash mismatch"),
            "哈希"
        },
        {
            new ArchivePackageException(
                ArchivePackageErrorCode.UnsupportedVersion, "unsupported version"),
            "版本"
        },
        {
            new ArchivePackageException(
                ArchivePackageErrorCode.LengthMismatch, "length mismatch"),
            "长度"
        },
        { new InvalidDataException("bad zip"), "不是有效的完整逻辑档案" },
        { new EndOfStreamException("truncated"), "截断" }
    };

    private sealed class ThrowingStageSource(Exception exception) : IArchiveTransferSource
    {
        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayerArchive>>([]);

        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
            Guid archiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameAccount>>([]);

        public Task<ArchiveWriteResult> ExportAsync(
            Stream destination,
            ArchiveExportScope scope,
            ArchiveGeneratorInfo generator,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IArchiveRestoreStagedPackage> StageAsync(
            Stream source,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IArchiveRestoreStagedPackage>(exception);

        public Task<ArchiveRestorePlan> PlanAsync(
            ArchiveRestorePlanRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveRestorePlanFreshness> CheckFreshnessAsync(
            ArchiveRestorePlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveRestoreApplyResult> ApplyAsync(
            ArchiveRestoreApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveRestoreReceipt?> FindReceiptAsync(
            OperationId operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ArchiveRestoreReceipt?>(null);

        public Task<ArchiveReplaceRecoveryPoint?> FindCurrentRecoveryPointAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ArchiveReplaceRecoveryPoint?>(null);
    }

    private sealed class TestWorkspace(string directory) : IArchiveTransferWorkspace
    {
        public string TemporaryDirectory { get; } = directory;

        public Task PublishAsync(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void DeleteTemporary(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
