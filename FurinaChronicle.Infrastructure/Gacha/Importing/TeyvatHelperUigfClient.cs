using System.Net;
using System.Text.Json;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Infrastructure.Gacha.Importing;

public sealed class TeyvatHelperUigfClient : ITeyvatHelperUigfClient, IDisposable
{
    public const string ExportUrl = "https://www.lelaer.com/outputGacha.php";
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public TeyvatHelperUigfClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false
            })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        ownsHttpClient = httpClient is null;
    }

    public async Task<TeyvatHelperUigfDownload> DownloadAsync(
        string uid,
        Uri gachaUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        ArgumentNullException.ThrowIfNull(gachaUrl);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ExportUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["uid"] = uid.Trim(),
                    ["gachaurl"] = gachaUrl.AbsoluteUri,
                    ["lang"] = "zh-Hans"
                })
        };
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "提瓦特小助手返回的 UIGF 文件过大，已停止下载。");
        }

        byte[] payload = await ReadLimitedAsync(
            response.Content,
            cancellationToken);
        byte[] jsonPayload = NormalizeAndValidate(payload);
        return new TeyvatHelperUigfDownload(
            $"TeyvatHelper-UIGF-{uid.Trim()}.json",
            jsonPayload);
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using Stream source = await content.ReadAsStreamAsync(
            cancellationToken);
        await using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    "提瓦特小助手返回的 UIGF 文件过大，已停止下载。");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        return destination.ToArray();
    }

    private static byte[] NormalizeAndValidate(byte[] payload)
    {
        int offset = SkipLeadingMarkup(payload);
        ReadOnlyMemory<byte> normalized = payload.AsMemory(offset);

        try
        {
            using JsonDocument document = JsonDocument.Parse(normalized);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResponse();
            }

            if (root.TryGetProperty("code", out JsonElement code) &&
                !IsSuccessCode(code))
            {
                string codeText = code.ToString();
                string? result = root.TryGetProperty(
                    "result",
                    out JsonElement resultElement)
                    ? GetSafeErrorText(resultElement)
                    : null;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result)
                        ? $"提瓦特小助手导出失败（{codeText}）。"
                        : $"提瓦特小助手导出失败（{codeText}）：{result}");
            }

            bool hasInfo = root.TryGetProperty(
                "info",
                out JsonElement info) &&
                info.ValueKind == JsonValueKind.Object;
            bool hasRecords = root.TryGetProperty("list", out _) ||
                root.TryGetProperty("hk4e", out _);
            if (!hasInfo || !hasRecords)
            {
                throw InvalidResponse();
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "提瓦特小助手返回的内容不是有效的 UIGF JSON。",
                exception);
        }

        return offset == 0
            ? payload
            : normalized.ToArray();
    }

    private static int SkipLeadingMarkup(byte[] payload)
    {
        int offset = payload.AsSpan().StartsWith(
            new byte[] { 0xEF, 0xBB, 0xBF })
            ? 3
            : 0;
        SkipAsciiWhitespace(payload, ref offset);

        while (TryConsume(payload, ref offset, "<br>") ||
            TryConsume(payload, ref offset, "<br/>") ||
            TryConsume(payload, ref offset, "<br />"))
        {
            SkipAsciiWhitespace(payload, ref offset);
        }

        return offset;
    }

    private static void SkipAsciiWhitespace(byte[] payload, ref int offset)
    {
        while (offset < payload.Length &&
            payload[offset] is (byte)' ' or (byte)'\t' or
                (byte)'\r' or (byte)'\n')
        {
            offset++;
        }
    }

    private static bool TryConsume(
        byte[] payload,
        ref int offset,
        string value)
    {
        if (payload.Length - offset < value.Length)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char actual = (char)payload[offset + index];
            if (char.ToUpperInvariant(actual) !=
                char.ToUpperInvariant(value[index]))
            {
                return false;
            }
        }

        offset += value.Length;
        return true;
    }

    private static bool IsSuccessCode(JsonElement code)
    {
        return code.ValueKind == JsonValueKind.Number &&
            code.TryGetInt32(out int number) && number == 0 ||
            code.ValueKind == JsonValueKind.String &&
            string.Equals(code.GetString(), "0", StringComparison.Ordinal);
    }

    private static string? GetSafeErrorText(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = result.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            text.Contains("authkey", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return text.Length <= 200 ? text : text[..200];
    }

    private static InvalidDataException InvalidResponse()
    {
        return new InvalidDataException(
            "提瓦特小助手返回的内容不是可导入的 UIGF 文件。");
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
