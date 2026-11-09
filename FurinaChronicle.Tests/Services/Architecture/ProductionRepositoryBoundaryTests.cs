// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Architecture;

public sealed class ProductionRepositoryBoundaryTests
{
    [Fact]
    public void GachaQueryRepository_DoesNotExposeLegacyBatchWriter()
    {
        Assert.Null(typeof(IGachaRecordRepository).GetMethod(
            "SaveBatchAsync"));
        Assert.Null(typeof(InMemoryGachaRecordRepository).GetMethod(
            "SaveBatchAsync"));
        Assert.Null(typeof(SqliteGachaRecordRepository).GetMethod(
            "SaveBatchAsync"));
    }

    [Fact]
    public void ParentRepositories_DoNotExposeCascadeDeleteToServices()
    {
        Assert.Null(typeof(IGameAccountRepository).GetMethod(
            "DeleteAsync"));
        Assert.Null(typeof(IPlayerArchiveRepository).GetMethod(
            "DeleteAsync"));
        Assert.Null(typeof(SqliteGameAccountRepository).GetMethod(
            "DeleteAsync"));
        Assert.Null(typeof(SqlitePlayerArchiveRepository).GetMethod(
            "DeleteAsync"));
    }
}
