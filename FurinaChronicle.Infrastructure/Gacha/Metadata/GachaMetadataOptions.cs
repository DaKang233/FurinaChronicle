namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed record GachaMetadataOptions
{
    public GachaMetadataOptions(
        string databasePath,
        TimeSpan? refreshInterval = null,
        string preferredLanguage = "chs")
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Metadata database path is required.", nameof(databasePath));
        }

        if (refreshInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "Refresh interval must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(preferredLanguage))
        {
            throw new ArgumentException("Preferred language is required.", nameof(preferredLanguage));
        }

        DatabasePath = databasePath;
        RefreshInterval = refreshInterval ?? TimeSpan.FromDays(7);
        PreferredLanguage = preferredLanguage.Trim().ToLowerInvariant();
    }

    public string DatabasePath { get; }

    public TimeSpan RefreshInterval { get; }

    public string PreferredLanguage { get; }
}
