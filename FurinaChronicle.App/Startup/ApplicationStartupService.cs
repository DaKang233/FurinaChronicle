// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
#if WINDOWS
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
#endif
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;
using FurinaChronicle.Services.Passport;
using System.Diagnostics;

namespace FurinaChronicle.App.Startup;

#if PHASE8B_VALIDATION
#pragma warning disable CS9113 // Production-only dependencies are intentionally idle in the isolated validation build.
#endif
public sealed class ApplicationStartupService(
#if WINDOWS
    SqliteDatabaseSession databaseSession,
#endif
    IGachaMetadataRefreshService metadataRefreshService,
    PassportCredentialMaintenanceService credentialMaintenanceService)
#if PHASE8B_VALIDATION
#pragma warning restore CS9113
#endif
{
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (!databaseSession.TryAcquire()) return false;
#endif
#if PHASE8B_VALIDATION
        await Task.CompletedTask;
#else
        GachaMetadataRefreshResult result =
            await metadataRefreshService.RefreshIfNeededAsync(
                GachaGame.GenshinImpact,
                force: false,
                cancellationToken);

        // 防御性检查，避免以后其他实现错误地返回“无数据但成功”。
        bool metadataAvailable = result.ContentUpdated || result.UsedExistingCache;

        if (!metadataAvailable)
        {
            throw new GachaMetadataUnavailableException("没有可用的原神角色和武器元数据。", new InvalidDataException("Metadata refresh completed without producing a usable cache."));
        }
#endif
        return true;
    }

    public async Task MaintainPassportAccountsAsync(
        CancellationToken cancellationToken = default)
    {
#if PHASE8B_VALIDATION
        await Task.CompletedTask;
        return;
#else
        try
        {
            PassportMaintenanceResult maintenance =
                await credentialMaintenanceService.MaintainAllAsync(
                    cancellationToken);
            if (maintenance.FailedAccountIds.Count > 0)
            {
                Debug.WriteLine(
                    $"Passport maintenance failed for {maintenance.FailedAccountIds.Count} account(s); existing credentials were retained.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Passport maintenance is best-effort and must not block offline use.
            Debug.WriteLine($"Passport maintenance skipped: {exception.Message}");
        }
#endif
    }
}
