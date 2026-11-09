// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Core.Archives;

internal static class UuidVersion5
{
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> namespaceBytes = stackalloc byte[16];
        if (!namespaceId.TryWriteBytes(
                namespaceBytes,
                bigEndian: true,
                out int bytesWritten) ||
            bytesWritten != namespaceBytes.Length)
        {
            throw new InvalidOperationException(
                "Unable to encode the UUID namespace.");
        }

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] input = GC.AllocateUninitializedArray<byte>(
            namespaceBytes.Length + nameBytes.Length);
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        Span<byte> hash = stackalloc byte[20];
        int hashLength = SHA1.HashData(input, hash);
        if (hashLength != hash.Length)
        {
            throw new InvalidOperationException(
                "Unable to calculate the UUID v5 hash.");
        }

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}
