// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Windows.Composition;

// WinUI credential vault. Credentials are encrypted with DPAPI (CurrentUser).
public sealed class DpapiPassportCredentialStore :
    IIsolatedCredentialStore,
    IPassportAccountStore
{
    private const int PayloadVersion = 1;
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("FurinaChronicle.UI4.WinUI.Credentials.v1");

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory;
    private readonly string filePath;
    private string? loadWarning;

    public DpapiPassportCredentialStore(WindowsPaths paths)
    {
        directory = Path.GetFullPath(paths.CredentialDirectory);
        filePath = Path.GetFullPath(paths.CredentialFilePath);
    }

    public string? LoadWarning => loadWarning;

    // IIsolatedCredentialStore: safe metadata only.
    public async Task<IReadOnlyList<IsolatedCredentialSummary>> ListAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken))
            .Entries
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new IsolatedCredentialSummary(
                item.Id,
                item.DisplayName,
                CredentialValidation.MaskMid(item.Mid),
                item.BoundRoleUid ?? (item.LoginMethod is null ? item.Aid : string.Empty),
                item.UpdatedAt,
                item.IsUsable,
                item.FailureReason,
                item.RoleUids,
                item.Realm == PassportRealm.MainlandChina &&
                    !string.IsNullOrWhiteSpace(item.SToken) && !string.IsNullOrWhiteSpace(item.Mid)))
            .ToArray();

    public async Task<Guid> SaveAsync(
        string sToken,
        string mid,
        string boundRoleUid,
        string? label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sToken);
        ArgumentNullException.ThrowIfNull(mid);
        ArgumentNullException.ThrowIfNull(boundRoleUid);
        string token = sToken.Trim();
        string normalizedMid = mid.Trim();
        string normalizedUid = boundRoleUid.Trim();
        string? validation = CredentialValidation.ValidateSToken(token) ??
            CredentialValidation.ValidateMid(normalizedMid) ??
            CredentialValidation.ValidateBoundUid(normalizedUid);
        if (validation is not null) throw new ArgumentException(validation);

        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            StoredEntry? existing = payload.Entries.FirstOrDefault(item =>
                item.LoginMethod is null && string.Equals(item.Aid, normalizedUid, StringComparison.Ordinal));
            Guid id = existing?.Id ?? Guid.NewGuid();
            StoredEntry entry = new(
                id,
                normalizedUid,
                normalizedMid,
                token,
                string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                existing?.DeviceId ?? Guid.NewGuid().ToString("D"),
                existing?.CreatedAt ?? now,
                now,
                true,
                null);
            List<StoredEntry> entries = payload.Entries
                .Where(item => item.Id != id)
                .Append(entry)
                .ToList();
            await WriteAsync(new Payload(PayloadVersion, entries), cancellationToken);
            return id;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            if (payload.Entries.All(item => item.Id != accountId)) return;
            await WriteAsync(
                new Payload(
                    PayloadVersion,
                    payload.Entries.Where(item => item.Id != accountId).ToList()),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(new Payload(PayloadVersion, []), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkUnusableAsync(
        Guid accountId,
        string safeReason,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            List<StoredEntry> entries = payload.Entries
                .Select(item => item.Id == accountId
                    ? item with
                    {
                        IsUsable = false,
                        FailureReason = SafeReason(safeReason),
                        UpdatedAt = DateTimeOffset.Now
                    }
                    : item)
                .ToList();
            await WriteAsync(new Payload(PayloadVersion, entries), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkUsableAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            List<StoredEntry> entries = payload.Entries
                .Select(item => item.Id == accountId
                    ? item with { IsUsable = true, FailureReason = null }
                    : item)
                .ToList();
            await WriteAsync(new Payload(PayloadVersion, entries), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // IPassportAccountStore: refresh service boundary only.
    public async Task<IReadOnlyList<PassportAccount>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).Entries.Select(ToAccount).ToArray();

    public async Task<PassportAccount?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).Entries
            .FirstOrDefault(item => item.Id == accountId) is { } entry
            ? ToAccount(entry)
            : null;

    public async Task SaveAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            StoredEntry? existing = payload.Entries.FirstOrDefault(item =>
                item.Id == account.Id);
            StoredEntry entry = FromAccount(account, existing);
            List<StoredEntry> entries = payload.Entries
                .Where(item => item.Id != account.Id)
                .Append(entry)
                .ToList();
            await WriteAsync(new Payload(PayloadVersion, entries), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> TrySaveIfUnchangedAsync(
        PassportAccount original,
        PassportAccount updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            StoredEntry? current = payload.Entries.FirstOrDefault(item =>
                item.Id == original.Id);
            if (current is null || current.UpdatedAt != original.UpdatedAt) return false;
            StoredEntry entry = FromAccount(updated, current);
            await WriteAsync(
                new Payload(
                    PayloadVersion,
                    payload.Entries
                        .Where(item => item.Id != updated.Id)
                        .Append(entry)
                        .ToList()),
                cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateRolesAsync(PassportAccount account, IReadOnlyList<PassportGameRole> roles,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Payload payload = await ReadCoreAsync(cancellationToken);
            int index = payload.Entries.FindIndex(item =>
                item.Id == account.Id && item.UpdatedAt == account.UpdatedAt);
            if (index < 0) return;
            payload.Entries[index] = payload.Entries[index] with
            {
                RoleUids = roles.Select(role => role.Uid).Distinct(StringComparer.Ordinal).ToArray()
            };
            await WriteAsync(payload, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Payload> ReadAsync(CancellationToken cancellationToken)
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

    private async Task<Payload> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            loadWarning = null;
            return new Payload(PayloadVersion, []);
        }

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(filePath, cancellationToken);
            byte[] plain = ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
            Payload? payload = JsonSerializer.Deserialize<Payload>(plain);
            if (payload is null || payload.Version != PayloadVersion)
            {
                throw new InvalidDataException();
            }

            loadWarning = null;
            return new Payload(
                PayloadVersion,
                payload.Entries.Where(IsValidEntry).ToList());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            loadWarning =
                "本机隔离宿主凭据文件无法解密或已损坏；已按空列表退化，" +
                "未删除档案。重新添加凭据会覆盖损坏文件。";
            return new Payload(PayloadVersion, []);
        }
    }

    private async Task WriteAsync(Payload payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] encrypted = ProtectedData.Protect(
            plain,
            Entropy,
            DataProtectionScope.CurrentUser);
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
        File.Move(temporary, filePath, overwrite: true);
        loadWarning = null;
    }

    private static bool IsValidEntry(StoredEntry? entry) =>
        entry is not null &&
        entry.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(entry.Aid) &&
        !string.IsNullOrWhiteSpace(entry.DeviceId) &&
        (!string.IsNullOrWhiteSpace(entry.SToken) || !string.IsNullOrWhiteSpace(entry.LToken) ||
            !string.IsNullOrWhiteSpace(entry.CookieToken));

    private static StoredEntry FromAccount(PassportAccount account, StoredEntry? existing) =>
        new(account.Id, account.Aid, account.Mid, account.Credentials.SToken,
            account.DisplayName, account.Device.DeviceId, account.CreatedAt, account.UpdatedAt,
            true, null, account.LoginMethod, account.Realm, account.Credentials.LToken,
            account.Credentials.CookieToken, account.Credentials.STokenUpdatedAt,
            account.Credentials.LTokenUpdatedAt, account.Credentials.CookieTokenUpdatedAt,
            account.Credentials.SessionVerifiedAt, account.Device.DeviceFingerprint,
            existing?.BoundRoleUid ?? (existing is { LoginMethod: null } ? existing.Aid : null),
            existing?.RoleUids);

    private static PassportAccount ToAccount(StoredEntry entry) =>
        new(
            entry.Id,
            entry.Aid,
            entry.Mid,
            entry.DisplayName,
            entry.LoginMethod ?? PassportLoginMethod.ManualCookie,
            new PassportCredentials(
                entry.SToken,
                entry.LToken,
                entry.CookieToken,
                entry.STokenUpdatedAt ?? (entry.LoginMethod is null ? entry.UpdatedAt : null),
                entry.LTokenUpdatedAt,
                entry.CookieTokenUpdatedAt,
                entry.SessionVerifiedAt),
            new PassportDeviceIdentity(entry.DeviceId, entry.DeviceFingerprint),
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.Realm);

    private static string SafeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "凭据已失效；请重新添加。"
            : reason.Length <= 160
                ? reason
                : reason[..160];

    private sealed record Payload(int Version, List<StoredEntry> Entries);

    private sealed record StoredEntry(
        Guid Id,
        string Aid,
        string? Mid,
        string? SToken,
        string? DisplayName,
        string DeviceId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        bool IsUsable,
        string? FailureReason,
        PassportLoginMethod? LoginMethod = null,
        PassportRealm Realm = PassportRealm.MainlandChina,
        string? LToken = null,
        string? CookieToken = null,
        DateTimeOffset? STokenUpdatedAt = null,
        DateTimeOffset? LTokenUpdatedAt = null,
        DateTimeOffset? CookieTokenUpdatedAt = null,
        DateTimeOffset? SessionVerifiedAt = null,
        string? DeviceFingerprint = null,
        string? BoundRoleUid = null,
        string[]? RoleUids = null)
    {
    }
}
