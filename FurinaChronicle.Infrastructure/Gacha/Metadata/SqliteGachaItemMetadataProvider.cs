using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed class SqliteGachaItemMetadataProvider(
    GachaMetadataDatabase database,
    IGachaMetadataRemoteSource remoteSource,
    IGachaLocalizationSource localizationSource,
    GachaMetadataOptions options,
    TimeProvider timeProvider)
    : IGachaItemMetadataProvider, IGachaMetadataRefreshService
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public async ValueTask<GachaItemMetadata?> FindByIdAsync(
        GachaGame game,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException("Item ID is required.", nameof(itemId));
        }

        await RefreshIfNeededAsync(game, cancellationToken: cancellationToken);

        (
            GachaMetadataItemRow? item,
            IReadOnlyList<GachaMetadataNameRow> names) =
            await database.FindAsync(
                game,
                itemId.Trim(),
                cancellationToken);

        if (item is null)
        {
            return null;
        }

        Dictionary<string, string> localizedNames = names
            .GroupBy(name => name.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name,
                StringComparer.OrdinalIgnoreCase);

        string? displayName = SelectDisplayName(localizedNames);
        if (displayName is null)
        {
            return null;
        }

        return new GachaItemMetadata(
            game,
            item.ItemId,
            displayName,
            item.ItemType,
            item.RankType,
            item.IconUrl,
            localizedNames);
    }

    public async Task<GachaMetadataRefreshResult> RefreshIfNeededAsync(
        GachaGame game,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            GachaMetadataStateRow? state =
                await database.GetStateAsync(game, cancellationToken);
            bool hasExistingCache =
                await database.HasItemsAsync(game, cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();

            if (!force &&
                hasExistingCache &&
                state is not null &&
                IsFresh(state, now))
            {
                return new GachaMetadataRefreshResult(
                    CheckAttempted: false,
                    ContentUpdated: false,
                    UsedExistingCache: true,
                    state.ContentSha256);
            }

            try
            {
                IReadOnlyList<GachaMetadataSourceItem> sourceItems =
                    await remoteSource.FetchAsync(game, cancellationToken);
                IReadOnlyDictionary<
                    string,
                    IReadOnlyDictionary<string, string>> localizations =
                    await localizationSource.LoadAsync(game, cancellationToken);

                (
                    GachaMetadataItemRow[] itemRows,
                    GachaMetadataNameRow[] nameRows) =
                    BuildSnapshot(game, sourceItems, localizations);

                string contentSha256 =
                    ComputeContentSha256(itemRows, nameRows);

                if (hasExistingCache &&
                    string.Equals(
                        state?.ContentSha256,
                        contentSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await database.MarkSuccessfulCheckAsync(
                        game,
                        now,
                        contentSha256,
                        cancellationToken);

                    return new GachaMetadataRefreshResult(
                        CheckAttempted: true,
                        ContentUpdated: false,
                        UsedExistingCache: true,
                        contentSha256);
                }

                await database.ReplaceSnapshotAsync(
                    game,
                    itemRows,
                    nameRows,
                    contentSha256,
                    now,
                    cancellationToken);

                return new GachaMetadataRefreshResult(
                    CheckAttempted: true,
                    ContentUpdated: true,
                    UsedExistingCache: false,
                    contentSha256);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (hasExistingCache)
            {
                await database.MarkCheckedAsync(
                    game,
                    now,
                    cancellationToken);

                return new GachaMetadataRefreshResult(
                    CheckAttempted: true,
                    ContentUpdated: false,
                    UsedExistingCache: true,
                    state?.ContentSha256);
            }
            catch (Exception exception)
            {
                throw new GachaMetadataUnavailableException(
                    "无法初始化原神角色和武器元数据。",
                    exception);
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private bool IsFresh(
        GachaMetadataStateRow state,
        DateTimeOffset now)
    {
        if (state.LastCheckUtcTicks <= 0)
        {
            return false;
        }

        var lastCheck = new DateTimeOffset(
            state.LastCheckUtcTicks,
            TimeSpan.Zero);
        TimeSpan elapsed = now - lastCheck;
        return elapsed >= TimeSpan.Zero &&
            elapsed < options.RefreshInterval;
    }

    private string? SelectDisplayName(
        IReadOnlyDictionary<string, string> localizedNames)
    {
        string[] preferredLanguages =
            [options.PreferredLanguage, "chs", "en"];

        foreach (string language in preferredLanguages.Distinct(
            StringComparer.OrdinalIgnoreCase))
        {
            if (localizedNames.TryGetValue(
                language,
                out string? localizedName))
            {
                return localizedName;
            }
        }

        return localizedNames
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }

    private static (
        GachaMetadataItemRow[] Items,
        GachaMetadataNameRow[] Names) BuildSnapshot(
        GachaGame game,
        IReadOnlyList<GachaMetadataSourceItem> sourceItems,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, string>> localizations)
    {
        if (sourceItems.Count == 0)
        {
            throw new InvalidDataException(
                "The remote metadata snapshot is empty.");
        }

        List<GachaMetadataItemRow> items = [];
        List<GachaMetadataNameRow> names = [];
        HashSet<string> itemIds = new(StringComparer.Ordinal);

        foreach (GachaMetadataSourceItem sourceItem in sourceItems
            .OrderBy(item => item.ItemId, StringComparer.Ordinal))
        {
            string itemId = sourceItem.ItemId.Trim();
            if (itemId.Length == 0 ||
                !itemId.All(char.IsAsciiDigit) ||
                !itemIds.Add(itemId) ||
                string.IsNullOrWhiteSpace(sourceItem.ItemType) ||
                sourceItem.RankType is < 1 or > 5)
            {
                throw new InvalidDataException(
                    $"Invalid or duplicate metadata item: {sourceItem.ItemId}.");
            }

            items.Add(new GachaMetadataItemRow
            {
                GameId = (int)game,
                ItemId = itemId,
                ItemType = sourceItem.ItemType.Trim(),
                RankType = sourceItem.RankType,
                IconUrl = NormalizeOptional(sourceItem.IconUrl)
            });

            Dictionary<string, string> itemNames =
                new(StringComparer.OrdinalIgnoreCase);

            if (localizations.TryGetValue(
                itemId,
                out IReadOnlyDictionary<string, string>? localizedNames))
            {
                foreach ((string language, string localizedName) in
                    localizedNames)
                {
                    string normalizedLanguage =
                        language.Trim().ToLowerInvariant();
                    string? normalizedName =
                        NormalizeOptional(localizedName);

                    if (normalizedLanguage.Length > 0 &&
                        normalizedName is not null)
                    {
                        itemNames[normalizedLanguage] = normalizedName;
                    }
                }
            }

            itemNames.TryAdd("chs", sourceItem.SourceName.Trim());

            foreach ((string language, string localizedName) in
                itemNames.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal))
            {
                names.Add(new GachaMetadataNameRow
                {
                    GameId = (int)game,
                    ItemId = itemId,
                    Language = language,
                    Name = localizedName
                });
            }
        }

        return ([.. items], [.. names]);
    }

    private static string ComputeContentSha256(
        IEnumerable<GachaMetadataItemRow> items,
        IEnumerable<GachaMetadataNameRow> names)
    {
        var canonical = new StringBuilder();

        foreach (GachaMetadataItemRow item in items
            .OrderBy(item => item.ItemId, StringComparer.Ordinal))
        {
            AppendField(canonical, item.GameId.ToString());
            AppendField(canonical, item.ItemId);
            AppendField(canonical, item.ItemType);
            AppendField(canonical, item.RankType?.ToString());
            AppendField(canonical, item.IconUrl);
        }

        foreach (GachaMetadataNameRow name in names
            .OrderBy(name => name.ItemId, StringComparer.Ordinal)
            .ThenBy(name => name.Language, StringComparer.Ordinal))
        {
            AppendField(canonical, name.GameId.ToString());
            AppendField(canonical, name.ItemId);
            AppendField(canonical, name.Language);
            AppendField(canonical, name.Name);
        }

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendField(
        StringBuilder builder,
        string? value)
    {
        value ??= string.Empty;
        builder
            .Append(value.Length)
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
