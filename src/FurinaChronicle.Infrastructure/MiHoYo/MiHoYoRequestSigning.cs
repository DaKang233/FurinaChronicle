// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Infrastructure.MiHoYo;

internal static class MiHoYoRequestSigning
{
    internal const string Lk2Salt = "sidQFEglajEz7FA0Aj7HQPV88zpf17SO";
    internal const string Cnk2Salt = "24cf11fb14a84f0683e7cadf7c788ba2";
    internal const string Cnx4Salt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    internal const string PassportSalt = "JwYDpKvLj6MrMqqYU6jTKF17KNO2PXoS";

    public static string CreateDs(string salt) => CreateDs(salt, numericNonce: false);

    /// <summary>
    /// Generation-1 signature: <c>{t},{r},{md5(salt&amp;t&amp;r)}</c>. The
    /// current role endpoints accept an alphanumeric nonce; the game-record
    /// transport uses the numeric form.
    /// </summary>
    public static string CreateDs(string salt, bool numericNonce)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string random = numericNonce
            ? RandomNumberGenerator.GetInt32(100000, 200000).ToString(CultureInfo.InvariantCulture)
            : RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 6);
        string source = $"salt={salt}&t={timestamp}&r={random}";
        string checksum = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return $"{timestamp},{random},{checksum}";
    }

    public static string CreateDsGen2(
        string salt,
        string body = "",
        string query = "",
        bool numericNonce = false)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string random = numericNonce
            ? RandomNumberGenerator.GetInt32(100001, 200000).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 6);
        string source =
            $"salt={salt}&t={timestamp}&r={random}&b={body}&q={query}";
        string checksum = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return $"{timestamp},{random},{checksum}";
    }
}
