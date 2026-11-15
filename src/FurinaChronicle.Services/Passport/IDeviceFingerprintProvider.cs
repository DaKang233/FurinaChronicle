// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Passport;

/// <summary>
/// Registers and returns the MiYouShe device fingerprint for a device
/// identifier. The fingerprint belongs to this client's own device context;
/// it is never copied from another client or another device.
/// </summary>
public interface IDeviceFingerprintProvider
{
    Task<string?> GetFingerprintAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}
