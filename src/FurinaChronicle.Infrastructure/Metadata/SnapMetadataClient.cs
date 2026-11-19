// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;

namespace FurinaChronicle.Infrastructure.Metadata;

/// <summary>
/// Versioned Snap.Metadata reader. The repository manifest is fetched with a
/// daily check interval; its content hash selects a cache version directory so
/// a refreshed repository never mixes old and new entries. Network failures
/// fall back to the last cached version.
/// </summary>
public sealed partial class SnapMetadataClient : IDisposable
{
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 32,
    };

    private readonly SnapMetadataOptions options;
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim versionGate = new(1, 1);
    private string? version;
    private Dictionary<int, WeaponEntry>? weapons;
    private Dictionary<int, TowerScheduleEntry>? abyssSchedules;
    private Dictionary<int, TowerFloorEntry>? abyssFloors;
    private Dictionary<int, ScheduleEntry>? theaterSchedules;
    private Dictionary<int, ScheduleEntry>? stygianSchedules;

    public SnapMetadataClient(
        SnapMetadataOptions options,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        ownsClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DomainItemMetadata?> ResolveAvatarAsync(
        string typeId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(typeId, out int id) || id <= 0)
        {
            return null;
        }
        AvatarEntry? avatar = await ReadJsonAsync<AvatarEntry>(
            $"Avatar/{id}.json", cancellationToken);
        if (avatar is null || string.IsNullOrWhiteSpace(avatar.Name))
        {
            return null;
        }
        return new DomainItemMetadata(
            InventoryItemKind.Character,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            avatar.Name.Trim(),
            avatar.Quality,
            IconUrl(avatar.Icon),
            "Snap.Metadata");
    }

    public async Task<DomainItemMetadata?> ResolveWeaponAsync(
        string typeId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(typeId, out int id) || id <= 0)
        {
            return null;
        }
        weapons ??= (await ReadJsonAsync<WeaponEntry[]>(
            "Weapon.json", cancellationToken) ?? [])
            .Where(weapon => weapon.Id > 0 && !string.IsNullOrWhiteSpace(weapon.Name))
            .GroupBy(weapon => weapon.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return weapons.TryGetValue(id, out WeaponEntry? weapon)
            ? new DomainItemMetadata(
                InventoryItemKind.Weapon,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                weapon.Name!.Trim(),
                weapon.RankLevel,
                IconUrl(weapon.Icon),
                "Snap.Metadata")
            : null;
    }

    public async Task<ChallengeScheduleMetadata?> ResolveScheduleAsync(
        ChallengeMode mode,
        string seasonId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(seasonId, out int id) || id <= 0)
        {
            return null;
        }
        if (mode == ChallengeMode.SpiralAbyss)
        {
            return await ResolveAbyssScheduleAsync(id, cancellationToken);
        }

        Dictionary<int, ScheduleEntry> schedules = mode switch
        {
            ChallengeMode.ImaginariumTheater => theaterSchedules ??=
                await ReadScheduleTableAsync(
                    "RoleCombatSchedule.json", cancellationToken),
            ChallengeMode.StygianOnslaught => stygianSchedules ??=
                await ReadScheduleTableAsync(
                    "HardChallengeSchedule.json", cancellationToken),
            _ => [],
        };
        return schedules.TryGetValue(id, out ScheduleEntry? schedule)
            ? new ChallengeScheduleMetadata(
                mode,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                schedule.Name,
                schedule.Begin,
                schedule.End,
                "Snap.Metadata",
                Theater: mode == ChallengeMode.ImaginariumTheater &&
                    (schedule.Elements is not null || schedule.InitialAvatars is not null || schedule.SpecialAvatars is not null)
                    ? new TheaterScheduleMetadata(
                        (schedule.Elements ?? []).Distinct().ToArray(),
                        ReadAvatarIds(schedule.InitialAvatars), ReadAvatarIds(schedule.SpecialAvatars)) : null)
            : null;
    }

    private static string[] DescribeEffects(string?[]? descriptions) =>
        (descriptions ?? [])
            .OfType<string>()
            .Select(text => text.Trim())
            .Where(text => text.Length > 0)
            .ToArray();

    private async Task<Dictionary<int, ScheduleEntry>> ReadScheduleTableAsync(
        string fileName,
        CancellationToken cancellationToken) =>
        (await ReadJsonAsync<ScheduleEntry[]>(fileName, cancellationToken) ?? [])
        .Where(schedule => schedule.Id > 0)
        .GroupBy(schedule => schedule.Id)
        .ToDictionary(group => group.Key, group => group.First());

    private static string[] ReadAvatarIds(long[]? values) =>
        (values ?? []).Where(id => id > 0).Distinct()
            .Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static string? IconUrl(string? icon) =>
        string.IsNullOrWhiteSpace(icon)
            ? null
            : $"https://enka.network/ui/{icon.Trim()}.png";

    private async Task<T?> ReadJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            byte[]? bytes = await ReadFileAsync(relativePath, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Optional display metadata cannot fail a saved-history query.
            return null;
        }
    }

    private async Task<byte[]?> ReadFileAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        string? currentVersion = await EnsureVersionAsync(cancellationToken);
        if (currentVersion is null)
        {
            return null;
        }
        string cachePath = Path.Combine(
            options.CacheDirectory, "versions", currentVersion,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(cachePath))
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        try
        {
            byte[]? downloaded = await DownloadAsync(
                relativePath, cancellationToken);
            if (downloaded is null)
            {
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string temporary = cachePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, downloaded, cancellationToken);
            File.Move(temporary, cachePath, overwrite: true);
            return downloaded;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<byte[]?> DownloadAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        string url = new Uri(options.BaseAddress, relativePath).AbsoluteUri;
        using HttpResponseMessage response = await httpClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxFileBytes)
        {
            return null;
        }
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(
            cancellationToken);
        return bytes.Length > MaxFileBytes ? null : bytes;
    }

    private async Task<string?> EnsureVersionAsync(
        CancellationToken cancellationToken)
    {
        if (version is not null)
        {
            return version;
        }
        await versionGate.WaitAsync(cancellationToken);
        try
        {
            if (version is not null)
            {
                return version;
            }
            Directory.CreateDirectory(options.CacheDirectory);
            string versionFile = Path.Combine(
                options.CacheDirectory, "current-version.txt");
            string checkedFile = Path.Combine(
                options.CacheDirectory, "last-check.txt");
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (File.Exists(versionFile) && File.Exists(checkedFile) &&
                DateTimeOffset.TryParse(
                    await File.ReadAllTextAsync(checkedFile, cancellationToken),
                    out DateTimeOffset lastCheck) &&
                now - lastCheck < options.ManifestCheckInterval)
            {
                version = (await File.ReadAllTextAsync(
                    versionFile, cancellationToken)).Trim();
                return version;
            }

            try
            {
                byte[]? manifest = await DownloadAsync(
                    "Meta.json", cancellationToken);
                if (manifest is null)
                {
                    return version = CachedVersion(versionFile);
                }
                string hash = Convert.ToHexString(
                    SHA256.HashData(manifest))[..16];
                version = hash;
                await File.WriteAllTextAsync(
                    versionFile, hash, cancellationToken);
                await File.WriteAllTextAsync(
                    checkedFile, now.ToString("O"), cancellationToken);
                return version;
            }
            catch (HttpRequestException)
            {
                return version = CachedVersion(versionFile);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                return version = CachedVersion(versionFile);
            }
        }
        finally
        {
            versionGate.Release();
        }
    }

    private static string? CachedVersion(string versionFile) =>
        File.Exists(versionFile)
            ? File.ReadAllText(versionFile).Trim()
            : null;

    public void Dispose()
    {
        versionGate.Dispose();
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    private sealed record AvatarEntry(
        int Id, string? Name, string? Icon, int? Quality);

    private sealed record WeaponEntry(
        int Id, string? Name, string? Icon, int? RankLevel);

    private sealed record TowerScheduleEntry(
        int Id, int[]? FloorIds, DateTimeOffset? Open, DateTimeOffset? Close,
        string? BuffName, string?[]? Descriptions);

    private sealed record ScheduleEntry(
        int Id,
        string? Name,
        DateTimeOffset? Begin,
        DateTimeOffset? End,
        int[]? Elements,
        long[]? InitialAvatars,
        long[]? SpecialAvatars);
}
