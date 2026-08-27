using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Infrastructure.Gacha.Refreshing;

public sealed class WindowsGachaCacheUrlProvider : IWindowsGachaCacheUrlProvider
{
    private static readonly byte[][] UrlPrefixes =
    [
        "https://webstatic.mihoyo.com/hk4e/event/e20190909gacha"u8.ToArray(),
        "https://gs.hoyoverse.com/genshin/event/e20190909gacha"u8.ToArray(),
        "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog"u8.ToArray(),
        "https://public-operation-hk4e-sg.hoyoverse.com/gacha_info/api/getGachaLog"u8.ToArray()
    ];

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<Uri> FindAsync(
        string gameInstallationPath,
        GameServerRegion region,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Web-cache gacha refresh is available only on Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gameInstallationPath);
        string cacheFile = FindCacheFile(gameInstallationPath, region);
        if (!File.Exists(cacheFile))
        {
            throw new FileNotFoundException(
                "Genshin web-cache file was not found.",
                cacheFile);
        }

        await using var stream = new FileStream(
            cacheFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue)
        {
            throw new IOException("Genshin web-cache file is too large to inspect.");
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        string? url = FindLastUrl(bytes);
        if (url is null)
        {
            throw new InvalidDataException(
                "No gacha URL was found in the Genshin web cache.");
        }

        return GachaRefreshUrl.Parse(url);
    }

    internal static string FindCacheFile(
        string gameInstallationPath,
        GameServerRegion region)
    {
        string root = File.Exists(gameInstallationPath)
            ? Path.GetDirectoryName(Path.GetFullPath(gameInstallationPath))!
            : Path.GetFullPath(gameInstallationPath);
        string[] dataFolders = region switch
        {
            GameServerRegion.ChinaOfficial or GameServerRegion.ChinaBilibili =>
                ["YuanShen_Data"],
            GameServerRegion.Unknown =>
                ["YuanShen_Data", "GenshinImpact_Data"],
            _ => ["GenshinImpact_Data"]
        };
        var candidates = new List<string>();
        foreach (string dataFolder in dataFolders)
        {
            string webCaches = Path.Combine(root, dataFolder, "webCaches");
            candidates.Add(Path.Combine(
                webCaches,
                "Cache",
                "Cache_Data",
                "data_2"));
            if (Directory.Exists(webCaches))
            {
                candidates.AddRange(Directory
                    .EnumerateDirectories(webCaches)
                    .Select(directory => Path.Combine(
                        directory,
                        "Cache",
                        "Cache_Data",
                        "data_2")));
            }
        }

        return candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? candidates[0];
    }

    internal static string? FindLastUrl(ReadOnlySpan<byte> bytes)
    {
        int bestIndex = -1;
        byte[]? bestPrefix = null;
        foreach (byte[] prefix in UrlPrefixes)
        {
            int index = bytes.LastIndexOf(prefix);
            if (index > bestIndex)
            {
                bestIndex = index;
                bestPrefix = prefix;
            }
        }

        if (bestIndex < 0 || bestPrefix is null)
        {
            return null;
        }

        int end = bestIndex + bestPrefix.Length;
        while (end < bytes.Length && bytes[end] is >= 0x20 and < 0x7F)
        {
            end++;
        }

        return Encoding.UTF8.GetString(bytes[bestIndex..end]).TrimEnd('#', '/', '\0');
    }
}
