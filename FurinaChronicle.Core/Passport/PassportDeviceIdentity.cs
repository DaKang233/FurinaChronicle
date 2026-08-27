namespace FurinaChronicle.Core.Passport;

public sealed record PassportDeviceIdentity(
    string DeviceId,
    string? DeviceFingerprint)
{
    public static PassportDeviceIdentity Create()
    {
        return new PassportDeviceIdentity(
            Guid.NewGuid().ToString("D"),
            DeviceFingerprint: null);
    }
}
