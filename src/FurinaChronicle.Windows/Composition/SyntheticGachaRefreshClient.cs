// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

#if DEBUG
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Windows.Composition;

// Explicit UI automation source for the existing isolated synthetic host only.
// Release builds do not contain this source; production composition never selects it.
internal sealed class SyntheticGachaRefreshClient(bool fail) : IGachaLogClient
{
    internal static IGachaLogClient? TryCreate() =>
        Environment.GetEnvironmentVariable("FURINA_UI1_REFRESH_SCENARIO") switch
        {
            "success" => new SyntheticGachaRefreshClient(false),
            "failure" => new SyntheticGachaRefreshClient(true),
            _ => null,
        };

    public async Task<GachaRemotePage> GetPageAsync(
        Uri sourceUrl, string gachaType, string? endId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1300, cancellationToken);
        if (fail && gachaType == "302")
            throw new HttpRequestException("Synthetic refresh failure.");
        if (endId is not null || gachaType is not ("301" or "302"))
            return new([], null);
        bool character = gachaType == "301";
        GachaRemoteRecord[] records =
        [
            new("100000001", character ? "990010003" : "990010006",
                character ? "合成五星角色" : "合成五星武器",
                character ? "10000089" : "11501", character ? "角色" : "武器", 5, gachaType,
                new DateTimeOffset(2026, 11, 19, 12, 1, 0, TimeSpan.FromHours(8))),
            new("100000001", character ? "990010002" : "990010005",
                character ? "合成四星角色" : "合成四星武器",
                character ? "10000016" : "11401", character ? "角色" : "武器", 4, gachaType,
                new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(8))),
        ];
        return new(records, records[^1].ExternalRecordId);
    }
}
#endif
