using System.Text.Json;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;
using Microsoft.Maui.Storage;

namespace FurinaChronicle.App.Persistence;

public sealed class SecureStoragePassportAccountStore(ISecureStorage secureStorage)
    : IPassportAccountStore, IDisposable
{
    private const string StorageKey = "passport-accounts-v2";
    private const string LegacyStorageKey = "passport-accounts-v1";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<PassportAccount>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PassportAccount?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Passport account ID is required.",
                nameof(accountId));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<PassportAccount> accounts =
                await LoadUnsafeAsync(cancellationToken);
            return accounts.FirstOrDefault(account => account.Id == accountId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<PassportAccount> accounts =
                (await LoadUnsafeAsync(cancellationToken)).ToList();
            int index = accounts.FindIndex(existing => existing.Id == account.Id);
            if (index >= 0)
            {
                accounts[index] = account;
            }
            else
            {
                accounts.Add(account);
            }

            string json = JsonSerializer.Serialize(accounts.Select(StoredAccount.FromDomain));
            cancellationToken.ThrowIfCancellationRequested();
            await secureStorage.SetAsync(StorageKey, json);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Passport account ID is required.",
                nameof(accountId));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            List<PassportAccount> accounts =
                (await LoadUnsafeAsync(cancellationToken))
                .Where(account => account.Id != accountId)
                .ToList();
            if (accounts.Count == 0)
            {
                secureStorage.Remove(StorageKey);
                secureStorage.Remove(LegacyStorageKey);
                return;
            }

            string json = JsonSerializer.Serialize(accounts.Select(StoredAccount.FromDomain));
            cancellationToken.ThrowIfCancellationRequested();
            await secureStorage.SetAsync(StorageKey, json);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<PassportAccount>> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? json = await secureStorage.GetAsync(StorageKey);
        bool loadedLegacyPayload = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            json = await secureStorage.GetAsync(LegacyStorageKey);
            loadedLegacyPayload = !string.IsNullOrWhiteSpace(json);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        StoredAccount[] stored =
            JsonSerializer.Deserialize<StoredAccount[]>(json) ?? [];
        PassportAccount[] accounts = stored
            .Select(item => item.ToDomain())
            .ToArray();
        if (loadedLegacyPayload)
        {
            string upgradedJson = JsonSerializer.Serialize(
                accounts.Select(StoredAccount.FromDomain));
            cancellationToken.ThrowIfCancellationRequested();
            await secureStorage.SetAsync(StorageKey, upgradedJson);
            secureStorage.Remove(LegacyStorageKey);
        }

        return accounts;
    }

    public void Dispose()
    {
        gate.Dispose();
    }

    private sealed record StoredAccount(
        Guid Id,
        string Aid,
        string? Mid,
        string? DisplayName,
        PassportLoginMethod LoginMethod,
        PassportRealm? Realm,
        string? SToken,
        string? LToken,
        string? CookieToken,
        DateTimeOffset? STokenUpdatedAt,
        DateTimeOffset? LTokenUpdatedAt,
        DateTimeOffset? CookieTokenUpdatedAt,
        DateTimeOffset? SessionVerifiedAt,
        string DeviceId,
        string? DeviceFingerprint,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public static StoredAccount FromDomain(PassportAccount account)
        {
            return new StoredAccount(
                account.Id,
                account.Aid,
                account.Mid,
                account.DisplayName,
                account.LoginMethod,
                account.Realm,
                account.Credentials.SToken,
                account.Credentials.LToken,
                account.Credentials.CookieToken,
                account.Credentials.STokenUpdatedAt,
                account.Credentials.LTokenUpdatedAt,
                account.Credentials.CookieTokenUpdatedAt,
                account.Credentials.SessionVerifiedAt,
                account.Device.DeviceId,
                account.Device.DeviceFingerprint,
                account.CreatedAt,
                account.UpdatedAt);
        }

        public PassportAccount ToDomain()
        {
            PassportRealm realm = Realm ?? InferLegacyRealm();
            return new PassportAccount(
                Id,
                Aid,
                Mid,
                DisplayName,
                LoginMethod,
                new PassportCredentials(
                    SToken,
                    LToken,
                    CookieToken,
                    STokenUpdatedAt,
                    LTokenUpdatedAt,
                    CookieTokenUpdatedAt,
                    SessionVerifiedAt),
                new PassportDeviceIdentity(DeviceId, DeviceFingerprint),
                CreatedAt,
                UpdatedAt,
                realm);
        }

        private PassportRealm InferLegacyRealm()
        {
            // v1 used the same Password enum value for both the removed
            // mainland web login and HoYoLAB login. Their generated device
            // identifiers are distinct: UUID for mainland, 53 chars overseas.
            return LoginMethod == PassportLoginMethod.OverseaPassword &&
                DeviceId.Length == 53
                    ? PassportRealm.Oversea
                    : PassportRealm.MainlandChina;
        }
    }
}
