// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Windows.Composition;

// Preferences-equivalent local selection store for the WinUI host. It is a page/domain
// selection used by ArchiveSelectionService; it is not a global mutable "current account".
public sealed class JsonFileArchiveSelectionStore(string path) : IArchiveSelectionStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath = Path.GetFullPath(path);

    public async Task<Guid?> LoadCurrentArchiveIdAsync(
        CancellationToken cancellationToken = default)
    {
        PersistedSelection persisted = await ReadAsync(cancellationToken);
        return persisted.CurrentArchiveId;
    }

    public async Task<ArchiveSelection?> LoadAsync(CancellationToken cancellationToken = default)
    {
        PersistedSelection persisted = await ReadAsync(cancellationToken);
        if (persisted.CurrentArchiveId is not Guid archiveId ||
            !persisted.Accounts.TryGetValue(archiveId, out Guid accountId))
        {
            return null;
        }

        return new ArchiveSelection(archiveId, accountId);
    }

    public async Task<ArchiveSelection?> LoadForArchiveAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default)
    {
        PersistedSelection persisted = await ReadAsync(cancellationToken);
        return persisted.Accounts.TryGetValue(playerArchiveId, out Guid accountId)
            ? new ArchiveSelection(playerArchiveId, accountId)
            : null;
    }

    public Task SaveCurrentArchiveIdAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            persisted => persisted with { CurrentArchiveId = playerArchiveId },
            cancellationToken);

    public Task SaveAsync(
        ArchiveSelection selection,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            persisted =>
            {
                var accounts = new Dictionary<Guid, Guid>(persisted.Accounts)
                {
                    [selection.PlayerArchiveId] = selection.GameAccountId
                };
                return persisted with
                {
                    CurrentArchiveId = selection.PlayerArchiveId,
                    Accounts = accounts
                };
            },
            cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(
            _ => new PersistedSelection(null, []),
            cancellationToken);

    private async Task<PersistedSelection> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MutateAsync(
        Func<PersistedSelection, PersistedSelection> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            PersistedSelection current = await ReadCoreAsync(cancellationToken);
            PersistedSelection next = mutation(current);
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(new PersistedDto(
                        next.CurrentArchiveId,
                        next.Accounts.ToDictionary(
                            pair => pair.Key.ToString("D"),
                            pair => pair.Value.ToString("D")))),
                    cancellationToken);
                File.Move(temporary, filePath, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PersistedSelection> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new PersistedSelection(null, []);
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken);
            PersistedDto? dto = JsonSerializer.Deserialize<PersistedDto>(json);
            if (dto is null) return new PersistedSelection(null, []);
            var accounts = new Dictionary<Guid, Guid>();
            foreach ((string key, string value) in dto.Accounts ?? [])
            {
                if (Guid.TryParse(key, out Guid archiveId) &&
                    Guid.TryParse(value, out Guid accountId))
                {
                    accounts[archiveId] = accountId;
                }
            }

            return new PersistedSelection(dto.CurrentArchiveId, accounts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // A corrupt device selection file degrades to "no selection"; it never blocks pages.
            return new PersistedSelection(null, []);
        }
    }

    private sealed record PersistedSelection(Guid? CurrentArchiveId, Dictionary<Guid, Guid> Accounts);

    private sealed record PersistedDto(
        Guid? CurrentArchiveId,
        Dictionary<string, string>? Accounts);
}
