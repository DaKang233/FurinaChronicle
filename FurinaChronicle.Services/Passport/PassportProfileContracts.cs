using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed record PassportGameRole(
    string GameBiz,
    string Region,
    string Uid,
    string Nickname,
    int Level,
    string RegionName)
{
    public string DisplayName =>
        $"{Nickname} · {RegionName} · Lv.{Level} · {Uid}";
}

public sealed record PassportAccountProfile(
    string DisplayName,
    Uri? AvatarUrl,
    IReadOnlyList<PassportGameRole> Roles);

public interface IMiHoYoAccountProfileClient
{
    Task<PassportAccountProfile> GetAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);
}

public sealed record PassportSelection(
    Guid PassportAccountId,
    string? GameUid);

public interface IPassportSelectionStore
{
    Task<PassportSelection?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PassportSelection selection,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
