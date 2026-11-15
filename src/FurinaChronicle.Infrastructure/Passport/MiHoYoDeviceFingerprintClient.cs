// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

/// <summary>
/// Registers a device fingerprint for this client's own device identifier via
/// the official MiYouShe device-fingerprint endpoint. The game-record
/// endpoints answer 5003 for a mainland session without one.
/// </summary>
public sealed class MiHoYoDeviceFingerprintClient : IDeviceFingerprintProvider, IDisposable
{
    private const string Endpoint =
        "https://public-data-api.mihoyo.com/device-fp/api/getFp";

    /// <summary>
    /// Describes this client's own device context for the registration
    /// endpoint. Values are generated for FurinaChronicle and are not copied
    /// from another client's fingerprint or device record.
    /// </summary>
    private static readonly string ExtFields = JsonSerializer.Serialize(new
    {
        proxyStatus = 0,
        isRoot = 0,
        deviceName = "furina-device",
        productName = "furina",
        model = "FT-01",
        brand = "Furina",
        manufacturer = "Furina",
        deviceType = "phone",
        hardware = "qcom",
        board = "furina",
        cpuType = "arm64-v8a",
        sdkVersion = "33",
        osVersion = "13",
        packageName = "com.mihoyo.hyperion",
        packageVersion = "2.20.1",
        networkType = "WiFi",
        isTablet = 0,
        emulatorStatus = 0,
        isMockLocation = 0,
        debugStatus = 0,
        screenSize = "1080x2400",
        buildType = "release",
        buildTags = "release-keys",
        ui_mode = "UI_MODE_TYPE_NORMAL",
        ramCapacity = "8192",
        romCapacity = "128",
        ramRemain = "4096",
        romRemain = "64",
        batteryStatus = 100,
        chargeStatus = 1,
        simState = 5,
        hasKeyboard = 0,
        ringMode = 2,
        isAirMode = 0,
        accelerometer = "",
        gyroscope = "",
        magnetometer = "",
    });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsClient;

    public MiHoYoDeviceFingerprintClient(HttpClient? httpClient = null)
    {
        ownsClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });
    }

    public async Task<string?> GetFingerprintAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!IsHeaderToken(deviceId))
        {
            return null;
        }

        var payload = new FingerprintRequest(
            RandomHex(16),
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture),
            "2",
            RandomHex(13),
            "bbs_cn",
            ExtFields,
            deviceId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions),
                    Encoding.UTF8,
                    "application/json"),
            };
            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await response.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
            FingerprintEnvelope? envelope = await JsonSerializer.DeserializeAsync<FingerprintEnvelope>(
                body, JsonOptions, timeout.Token);
            return envelope is { Retcode: 0, Data.DeviceFp: { } fingerprint } &&
                IsHeaderToken(fingerprint)
                ? fingerprint
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RandomHex(int length)
    {
        const string alphabet = "0123456789abcdef";
        var builder = new StringBuilder(length);
        for (int index = 0; index < length; index++)
        {
            builder.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        }

        return builder.ToString();
    }

    private static bool IsHeaderToken(string value) =>
        value.Length > 0 && value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'));

    private sealed record FingerprintRequest(
        string DeviceId,
        string SeedId,
        string SeedTime,
        string Platform,
        string DeviceFp,
        string AppName,
        string ExtFields,
        string BbsDeviceId);

    private sealed record FingerprintEnvelope(int? Retcode, FingerprintData? Data);

    private sealed record FingerprintData(string? DeviceFp);

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }
}
