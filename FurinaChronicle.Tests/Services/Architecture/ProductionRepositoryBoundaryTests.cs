// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Tests.Services.Architecture;

public sealed class ProductionRepositoryBoundaryTests
{
    [Fact]
    public void GachaQueryRepository_DoesNotExposeLegacyBatchWriter()
    {
        Assert.Null(typeof(IGachaRecordRepository).GetMethod(
            "SaveBatchAsync"));
    }

    [Fact]
    public void ParentRepositories_DoNotExposeCascadeDeleteToServices()
    {
        Assert.Null(typeof(IGameAccountRepository).GetMethod(
            "DeleteAsync"));
        Assert.Null(typeof(IPlayerArchiveRepository).GetMethod(
            "DeleteAsync"));
    }
}
