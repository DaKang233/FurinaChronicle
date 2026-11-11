// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class MiYouSheSpiralAbyssSourceTests
{
    private static readonly GameRoleNaturalIdentity Role =
        new("hk4e_cn", "cn_gf01", "100000001");

    [Theory]
    [InlineData(0, SpiralAbyssCollectionStatus.Empty)]
    [InlineData(-100, SpiralAbyssCollectionStatus.AuthenticationRequired)]
    [InlineData(1034, SpiralAbyssCollectionStatus.VerificationRequired)]
    [InlineData(10101, SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(-110, SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(-1, SpiralAbyssCollectionStatus.Empty)]
    [InlineData(10102, SpiralAbyssCollectionStatus.AccessDenied)]
    [InlineData(5003, SpiralAbyssCollectionStatus.AccessDenied)]
    public async Task FetchUsesCurrentScheduleAndMapsBusinessResult(
        int retcode, SpiralAbyssCollectionStatus expected)
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api-takumi-record.mihoyo.com", request.RequestUri!.Host);
            Assert.Equal("?role_id=100000001&schedule_type=1&server=cn_gf01", request.RequestUri.Query);
            Assert.Equal("ltuid=1111; ltoken=fixture", Assert.Single(request.Headers.GetValues("Cookie")));
            string[] signature = Assert.Single(request.Headers.GetValues("DS")).Split(',');
            Assert.Equal(3, signature.Length);
            Assert.Matches("^[0-9]{6}$", signature[1]);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"retcode":{{{retcode}}},"data":{"schedule_id":1,"floors":[]}}"""),
            };
        }));
        using var source = new MiYouSheSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchCurrentAsync(Passport("fixture"), Role, default);

        Assert.Equal(expected, result.Status);
        Assert.Equal(retcode == 0 ? null : retcode, result.ProviderCode);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fixture; other=fixture")]
    public async Task RejectsMissingOrInvalidLTokenBeforeSending(string? lToken)
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new InvalidOperationException("Unexpected network request.")));
        using var source = new MiYouSheSpiralAbyssSource(TimeProvider.System, client);

        Assert.Equal(SpiralAbyssCollectionStatus.AuthenticationRequired,
            (await source.FetchCurrentAsync(Passport(lToken), Role, default)).Status);
    }

    private static PassportAccount Passport(string? lToken) =>
        new(Guid.NewGuid(), "1111", null, null, PassportLoginMethod.QrCode,
            new PassportCredentials("fixture", lToken, null, null, null, null, null),
            PassportDeviceIdentity.Create(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
