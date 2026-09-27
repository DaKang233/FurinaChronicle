namespace FurinaChronicle.Core.Passport;

using System.Security.Cryptography;

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

    public static PassportDeviceIdentity CreateOversea()
    {
        return new PassportDeviceIdentity(
            RandomNumberGenerator.GetString(
                "abcdefghijklmnopqrstuvwxyz0123456789",
                53),
            DeviceFingerprint: null);
    }
}
