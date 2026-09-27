namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed record GachaItemIconCacheOptions
{
    public GachaItemIconCacheOptions(
        string directoryPath,
        TimeSpan? refreshInterval = null,
        long maximumFileBytes = 10 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException(
                "Icon cache directory is required.",
                nameof(directoryPath));
        }
        if (refreshInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        }
        if (maximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        RefreshInterval = refreshInterval ?? TimeSpan.FromDays(30);
        MaximumFileBytes = maximumFileBytes;
    }

    public string DirectoryPath { get; }

    public TimeSpan RefreshInterval { get; }

    public long MaximumFileBytes { get; }
}
