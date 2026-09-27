using System.Globalization;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Infrastructure.Gacha.Refreshing;

public sealed class MiHoYoGachaLogClient : IGachaLogClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan minimumRequestInterval;
    private readonly IReadOnlyList<TimeSpan> rateLimitRetryDelays;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private DateTimeOffset? lastRequestAt;

    public MiHoYoGachaLogClient(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        TimeSpan? minimumRequestInterval = null,
        IReadOnlyList<TimeSpan>? rateLimitRetryDelays = null)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.minimumRequestInterval = minimumRequestInterval ??
            TimeSpan.FromMilliseconds(1500);
        this.rateLimitRetryDelays = rateLimitRetryDelays ??
            [
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            ];
    }

    public async Task<GachaRemotePage> GetPageAsync(
        Uri sourceUrl,
        string gachaType,
        string? endId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(gachaType);
        Uri requestUrl = BuildRequestUrl(sourceUrl, gachaType, endId);
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                await WaitForRequestSlotAsync(cancellationToken);
                lastRequestAt = timeProvider.GetUtcNow();
                using HttpResponseMessage response = await httpClient.GetAsync(
                    requestUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;
                int retcode = root.GetProperty("retcode").GetInt32();
                if (retcode == -110 && attempt < rateLimitRetryDelays.Count)
                {
                    await Task.Delay(
                        rateLimitRetryDelays[attempt],
                        timeProvider,
                        cancellationToken);
                    continue;
                }

                if (retcode != 0)
                {
                    string message = root.TryGetProperty(
                        "message",
                        out JsonElement messageElement)
                        ? messageElement.GetString() ?? "Unknown error"
                        : "Unknown error";
                    throw new InvalidOperationException(
                        $"MiHoYo getGachaLog failed ({retcode}): {message}");
                }

                return ParsePage(root, gachaType);
            }
        }
        finally
        {
            requestGate.Release();
        }
    }

    private static GachaRemotePage ParsePage(
        JsonElement root,
        string gachaType)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind == JsonValueKind.Null ||
            !data.TryGetProperty("list", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return new GachaRemotePage([], NextEndId: null);
        }

        var records = new List<GachaRemoteRecord>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            string uid = GetString(item, "uid") ?? string.Empty;
            string id = GetString(item, "id")
                ?? throw new InvalidDataException("Gacha record does not contain id.");
            string type = GetString(item, "gacha_type") ?? gachaType;
            string timeText = GetString(item, "time")
                ?? throw new InvalidDataException($"Gacha record {id} does not contain time.");
            records.Add(new GachaRemoteRecord(
                uid,
                id,
                GetString(item, "name"),
                GetString(item, "item_id"),
                GetString(item, "item_type"),
                ParseInt(GetString(item, "rank_type")),
                type,
                ParseTime(timeText, uid),
                ParseInt(GetString(item, "count")) ?? 1));
        }

        return new GachaRemotePage(
            records,
            records.Count < 20 ? null : records[^1].ExternalRecordId);
    }

    private async Task WaitForRequestSlotAsync(
        CancellationToken cancellationToken)
    {
        if (lastRequestAt is not DateTimeOffset previous)
        {
            return;
        }

        TimeSpan remaining = minimumRequestInterval -
            (timeProvider.GetUtcNow() - previous);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }

    internal static Uri BuildRequestUrl(
        Uri sourceUrl,
        string gachaType,
        string? endId)
    {
        IReadOnlyDictionary<string, string> sourceQuery =
            GachaRefreshUrl.ParseQuery(sourceUrl.Query);
        bool oversea = sourceUrl.Host.Contains("hoyoverse", StringComparison.OrdinalIgnoreCase) ||
            sourceQuery.TryGetValue("game_biz", out string? gameBiz) &&
            string.Equals(gameBiz, "hk4e_global", StringComparison.OrdinalIgnoreCase);
        string endpoint = oversea
            ? "https://public-operation-hk4e-sg.hoyoverse.com/gacha_info/api/getGachaLog"
            : "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog";

        var query = new Dictionary<string, string>(sourceQuery, StringComparer.OrdinalIgnoreCase)
        {
            ["gacha_type"] = gachaType,
            ["page"] = "1",
            ["size"] = "20",
            ["end_id"] = string.IsNullOrWhiteSpace(endId) ? "0" : endId
        };
        string queryText = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{endpoint}?{queryText}");
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : null;
    }

    private static DateTimeOffset ParseTime(string value, string uid)
    {
        if (!DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime localTime))
        {
            throw new InvalidDataException($"Unsupported gacha record time: {value}");
        }

        TimeSpan offset = GameServerRegionResolver.Resolve(uid) switch
        {
            GameServerRegion.America => TimeSpan.FromHours(-5),
            GameServerRegion.Europe => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(8)
        };
        return new DateTimeOffset(localTime, offset);
    }

    public void Dispose()
    {
        requestGate.Dispose();
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
