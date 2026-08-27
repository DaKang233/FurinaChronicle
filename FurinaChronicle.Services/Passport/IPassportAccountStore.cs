using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public interface IPassportAccountStore
{
    Task<IReadOnlyList<PassportAccount>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<PassportAccount?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
