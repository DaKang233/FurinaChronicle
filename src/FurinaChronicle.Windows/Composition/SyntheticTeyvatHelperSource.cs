// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

#if DEBUG
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Windows.Composition;

// Explicit isolated-host validation source. Only two counters are written, never secrets or a URL.
internal sealed class SyntheticTeyvatHelperSource(string directory, bool fail, bool wrongRole = false)
    : ISTokenGachaUrlProvider, ITeyvatHelperUigfClient
{
    private int generated;
    private int downloaded;
    internal static SyntheticTeyvatHelperSource? TryCreate(WindowsHostEnvironment environment) =>
        !environment.IsSynthetic ? null : Environment.GetEnvironmentVariable("FURINA_UI1_TEYVAT_SCENARIO") switch
        {
            "success" => new(environment.RootDirectory, false),
            "failure" => new(environment.RootDirectory, true),
            "wrong-role" => new(environment.RootDirectory, false, true),
            _ => null,
        };

    public Task<Uri> CreateAsync(PassportAccount passportAccount, GameAccount gameAccount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        generated++;
        SaveCounters();
        return Task.FromResult(new Uri(
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic-" +
            Guid.NewGuid().ToString("N") + "&region=cn_gf01"));
    }

    public Task<TeyvatHelperUigfDownload> DownloadAsync(string uid, Uri gachaUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        downloaded++;
        SaveCounters();
        if (fail) throw new HttpRequestException("Synthetic helper response failure.");
        if (wrongRole) uid = uid == "100000002" ? "100000001" : "100000002";
        string time = $"2026-11-19 12:00:{Math.Min(downloaded, 59):D2}";
        object Item(string id, string name, string itemId, string type, string rank) => new
        {
            id, name, item_id = itemId, item_type = type, rank_type = rank,
            count = "1", time, gacha_type = "301", uigf_gacha_type = "301",
        };
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            info = new { uid, uigf_version = "v2.2", lang = "zh-cn", export_timestamp = 1795000000 },
            list = new[]
            {
                Item("990020001", "合成小助手角色", "10000089", "角色", "5"),
                Item("990020002", "合成小助手武器", "11401", "武器", "4"),
            },
        });
        return Task.FromResult(new TeyvatHelperUigfDownload("SyntheticHelper-UIGF.json", content));
    }

    private void SaveCounters() => File.WriteAllText(Path.Combine(directory, "synthetic-teyvat-counters.json"),
        JsonSerializer.Serialize(new { generated, downloaded }));
}
#endif
