using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Remote;

public sealed class GenshinCalculatorMetadataSource : IGachaMetadataRemoteSource, IDisposable
{
    public const string AvatarListUrl =
        "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/avatar/list";

    public const string WeaponListUrl =
        "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/weapon/list";

    private static readonly Uri CalculatorReferrer =
        new("https://act.mihoyo.com/ys/event/calculator/index.html");

    private readonly HttpClient httpClient;
    private readonly IReadOnlyList<TimeSpan> retryDelays;
    private readonly bool ownsHttpClient;

    public GenshinCalculatorMetadataSource(
        HttpClient? httpClient = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
        this.retryDelays = retryDelays ??
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];
    }

    public async Task<IReadOnlyList<GachaMetadataSourceItem>> FetchAsync(
        GachaGame game,
        CancellationToken cancellationToken = default)
    {
        if (game != GachaGame.GenshinImpact)
        {
            throw new NotSupportedException($"Metadata source does not support game {game}.");
        }

        IReadOnlyList<GachaMetadataSourceItem> avatars =
            await FetchWithRetryAsync(
                AvatarListUrl,
                new { page = 1, size = 1000, is_all = true },
                "Avatar",
                "avatar_level",
                cancellationToken);

        IReadOnlyList<GachaMetadataSourceItem> weapons =
            await FetchWithRetryAsync(
                WeaponListUrl,
                new { page = 1, size = 1000, weapon_levels = new[] { 1, 2, 3, 4, 5 } },
                "Weapon",
                "weapon_level",
                cancellationToken);

        if (avatars.Count == 0 || weapons.Count == 0)
        {
            throw new InvalidDataException(
                "The calculator API returned an incomplete metadata snapshot.");
        }

        return avatars
            .Concat(weapons)
            .GroupBy(item => item.ItemId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.ItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<GachaMetadataSourceItem>> FetchWithRetryAsync(
        string url,
        object payload,
        string itemType,
        string rankProperty,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        int attemptCount = retryDelays.Count + 1;

        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            try
            {
                return await FetchListAsync(
                    url,
                    payload,
                    itemType,
                    rankProperty,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is (HttpRequestException or JsonException or InvalidDataException) &&
                attempt < attemptCount - 1)
            {
                lastException = exception;
                TimeSpan delay = retryDelays[attempt];
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        throw lastException ??
            new InvalidDataException("Unable to download calculator metadata.");
    }

    private async Task<IReadOnlyList<GachaMetadataSourceItem>> FetchListAsync(
        string url,
        object payload,
        string itemType,
        string rankProperty,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Referrer = CalculatorReferrer;
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "FurinaChronicle/1.0");

        using HttpResponseMessage response =
            await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream body =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document =
            await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("list", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The calculator API response is missing data.list.");
        }

        List<GachaMetadataSourceItem> result = [];
        foreach (JsonElement item in list.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? itemId = ReadStringOrInteger(item, "id");
            string? name = ReadString(item, "name");
            int? rankType = ReadInteger(item, rankProperty);

            if (itemId is null ||
                name is null ||
                rankType is not (>= 1 and <= 5) ||
                string.Equals(name, "Traveler", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new GachaMetadataSourceItem(
                itemId,
                name,
                itemType,
                rankType.Value,
                ReadString(item, "icon")));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException(
                $"The calculator API returned no valid {itemType} records.");
        }

        return result;
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? ReadStringOrInteger(
        JsonElement item,
        string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return text is not null && text.All(char.IsAsciiDigit)
            ? text
            : null;
    }

    private static int? ReadInteger(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number))
        {
            return number;
        }

        return int.TryParse(
            value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
