// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Infrastructure.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Passport;

public sealed class MiHoYoDeviceFingerprintClientTests
{
    [Fact]
    public async Task RegistersAndReturnsFingerprintForTheDevice()
    {
        string? requestBody = null;
        using var httpClient = new HttpClient(new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("public-data-api.mihoyo.com", request.RequestUri!.Host);
            Assert.Equal("/device-fp/api/getFp", request.RequestUri.AbsolutePath);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            requestBody = await request.Content.ReadAsStringAsync();
            return Json("""{"retcode":0,"message":"OK","data":{"device_fp":"0123456789abc"}}""");
        }));
        using var client = new MiHoYoDeviceFingerprintClient(httpClient);

        string? fingerprint = await client.GetFingerprintAsync("fixture-device-0001");

        Assert.Equal("0123456789abc", fingerprint);
        Assert.NotNull(requestBody);
        Assert.Contains("\"bbs_device_id\":\"fixture-device-0001\"", requestBody);
        Assert.Contains("\"app_name\":\"bbs_cn\"", requestBody);
        Assert.Contains("\"ext_fields\":", requestBody);
        Assert.Contains("packageName", requestBody);
        Assert.Contains("\"platform\":\"2\"", requestBody);
    }

    [Theory]
    [InlineData("""{"retcode":1,"message":"denied"}""")]
    [InlineData("""{"retcode":0,"data":null}""")]
    [InlineData("""{"retcode":0,"data":{"device_fp":""}}""")]
    [InlineData("{")]
    public async Task ReturnsNullWhenTheServiceDoesNotIssueAValidFingerprint(
        string content)
    {
        using var httpClient = new HttpClient(new RecordingHandler(
            _ => Task.FromResult(Json(content))));
        using var client = new MiHoYoDeviceFingerprintClient(httpClient);

        Assert.Null(await client.GetFingerprintAsync("fixture-device-0001"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fixture; invalid")]
    [InlineData("\"quoted\"")]
    public async Task RejectsInvalidDeviceIdentifierWithoutSending(string deviceId)
    {
        using var httpClient = new HttpClient(new RecordingHandler(
            _ => throw new InvalidOperationException("Unexpected network request.")));
        using var client = new MiHoYoDeviceFingerprintClient(httpClient);

        Assert.Null(await client.GetFingerprintAsync(deviceId));
    }

    [Fact]
    public async Task NetworkFailureReturnsNull()
    {
        using var httpClient = new HttpClient(new RecordingHandler(
            _ => throw new HttpRequestException("offline")));
        using var client = new MiHoYoDeviceFingerprintClient(httpClient);

        Assert.Null(await client.GetFingerprintAsync("fixture-device-0001"));
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
