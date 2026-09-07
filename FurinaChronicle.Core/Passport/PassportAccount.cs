namespace FurinaChronicle.Core.Passport;

public sealed class PassportAccount
{
    public PassportAccount(
        Guid id,
        string aid,
        string? mid,
        string? displayName,
        PassportLoginMethod loginMethod,
        PassportCredentials credentials,
        PassportDeviceIdentity device,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        PassportRealm realm = PassportRealm.MainlandChina)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Passport account ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(aid);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.DeviceId);
        if (!Enum.IsDefined(realm))
        {
            throw new ArgumentOutOfRangeException(nameof(realm));
        }

        Id = id;
        Aid = aid.Trim();
        Mid = string.IsNullOrWhiteSpace(mid) ? null : mid.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        LoginMethod = loginMethod;
        Credentials = credentials;
        Device = device;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Realm = realm;
    }

    public Guid Id { get; }

    public string Aid { get; }

    public string? Mid { get; }

    public string? DisplayName { get; }

    public PassportLoginMethod LoginMethod { get; }

    public PassportCredentials Credentials { get; }

    public PassportDeviceIdentity Device { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public PassportRealm Realm { get; }

    public PassportAccount WithCredentials(
        PassportCredentials credentials,
        DateTimeOffset updatedAt)
    {
        return new PassportAccount(
            Id,
            Aid,
            Mid,
            DisplayName,
            LoginMethod,
            credentials,
            Device,
            CreatedAt,
            updatedAt,
            Realm);
    }

    public override string ToString()
    {
        return $"PassportAccount(Id={Id:D}, Aid={Aid}, Realm={Realm}, LoginMethod={LoginMethod})";
    }
}
