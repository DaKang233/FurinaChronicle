// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Presentation.Gacha;

/// <summary>Device-local page preferences. Calls are queued from the page dispatcher, never facts.</summary>
public sealed class GachaSelectionPreferences(IArchiveSelectionStore store)
{
    private Task pending = Task.CompletedTask;
    private (Guid Archive, Guid? Account)? lastQueued;
    public string? Warning { get; private set; }

    public async Task<Guid?> LoadArchiveAsync(CancellationToken token)
    {
        await pending.WaitAsync(token).ConfigureAwait(false);
        try { return await store.LoadCurrentArchiveIdAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { Warning = "无法恢复上次选择；本地数据未改变"; return null; }
    }

    public async Task<Guid?> LoadAccountAsync(Guid archive, CancellationToken token)
    {
        await pending.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ArchiveSelection? saved = await store.LoadForArchiveAsync(archive, token).ConfigureAwait(false);
            return saved?.PlayerArchiveId == archive ? saved.GameAccountId : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { Warning = "无法恢复上次选择；本地数据未改变"; return null; }
    }

    public Task RememberAsync(Guid archive, Guid? account)
    {
        if (archive == Guid.Empty || account == Guid.Empty)
            throw new ArgumentException("Selection requires valid local identifiers.");
        var selection = (archive, account);
        if (lastQueued == selection && Warning is null) return pending;
        lastQueued = selection;
        return pending = SaveAfterAsync(pending, archive, account);
    }

    public Task FlushAsync() => pending;

    private async Task SaveAfterAsync(Task previous, Guid archive, Guid? account)
    {
        await previous.ConfigureAwait(false);
        try
        {
            if (account is Guid id)
                await store.SaveAsync(new(archive, id)).ConfigureAwait(false);
            else
                await store.SaveCurrentArchiveIdAsync(archive).ConfigureAwait(false);
            Warning = null;
        }
        catch (Exception)
        {
            // Do not leak filesystem exceptions/paths or fail a successful business operation.
            Warning = "无法保存上次选择；下次启动可能需要重新选择，已有数据未改变";
        }
    }
}
