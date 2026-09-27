using FurinaChronicle.Services.Passport;
using Microsoft.Maui.Storage;

namespace FurinaChronicle.App.Persistence;

public sealed class PreferencesPassportSelectionStore(IPreferences preferences)
    : IPassportSelectionStore
{
    private const string SelectionKey = "passport-selection-v1";

    public Task<PassportSelection?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string value = preferences.Get(SelectionKey, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<PassportSelection?>(null);
        }

        string[] parts = value.Split('|', count: 2);
        if (!Guid.TryParse(parts[0], out Guid accountId) ||
            accountId == Guid.Empty)
        {
            return Task.FromResult<PassportSelection?>(null);
        }

        string? uid = parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1]
            : null;
        return Task.FromResult<PassportSelection?>(
            new PassportSelection(accountId, uid));
    }

    public Task SaveAsync(
        PassportSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.PassportAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Passport account ID is required.",
                nameof(selection));
        }

        cancellationToken.ThrowIfCancellationRequested();
        preferences.Set(
            SelectionKey,
            $"{selection.PassportAccountId:D}|{selection.GameUid ?? string.Empty}");
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        preferences.Remove(SelectionKey);
        return Task.CompletedTask;
    }
}
