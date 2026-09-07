using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Infrastructure.MiHoYo;

internal static class MiHoYoRequestSigning
{
    internal const string Lk2Salt = "sidQFEglajEz7FA0Aj7HQPV88zpf17SO";
    internal const string PassportSalt = "JwYDpKvLj6MrMqqYU6jTKF17KNO2PXoS";

    public static string CreateDs(string salt)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string random = RandomNumberGenerator.GetString(
            "abcdefghijklmnopqrstuvwxyz0123456789",
            6);
        string source = $"salt={salt}&t={timestamp}&r={random}";
        string checksum = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return $"{timestamp},{random},{checksum}";
    }

    public static string CreateDsGen2(
        string salt,
        string body = "",
        string query = "")
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string random = RandomNumberGenerator.GetString(
            "abcdefghijklmnopqrstuvwxyz0123456789",
            6);
        string source =
            $"salt={salt}&t={timestamp}&r={random}&b={body}&q={query}";
        string checksum = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return $"{timestamp},{random},{checksum}";
    }
}
