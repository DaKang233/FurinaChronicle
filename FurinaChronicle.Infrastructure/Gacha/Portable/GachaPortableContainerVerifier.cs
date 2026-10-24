// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

internal static class GachaPortableContainerVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = Services.Gacha.Portable.GachaPortableLimits
            .DefaultMaximumJsonDepth,
    };

    public static async Task VerifyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(
            input,
            ZipArchiveMode.Read,
            leaveOpen: true);

        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw GachaPortableRules.Error(
                    Services.Gacha.Portable.GachaPortableErrorCode.InvalidPackage,
                    $"The staged package contains duplicate entry {entry.FullName}.");
            }
        }

        if (!entries.TryGetValue(
                GachaPortableRules.ManifestPath,
                out ZipArchiveEntry? manifestEntry))
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.MissingEntry,
                "The staged package does not contain manifest.json.");
        }

        GachaPortableManifestDto manifest;
        await using (Stream manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<
                GachaPortableManifestDto>(
                manifestStream,
                JsonOptions,
                cancellationToken)
                ?? throw GachaPortableRules.Error(
                    Services.Gacha.Portable.GachaPortableErrorCode.InvalidPackage,
                    "The staged manifest is invalid.");
        }

        if (manifest.Accounts is null || manifest.Scope is null)
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.InvalidPackage,
                "The staged manifest is incomplete.");
        }

        int recordCount = 0;
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            GachaPortableRules.ManifestPath,
        };
        foreach (GachaPortableAccountManifestDto? account in manifest.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (account?.UigfPath is null ||
                account.SupplementPath is null ||
                account.UigfSha256 is null ||
                account.SupplementSha256 is null)
            {
                throw GachaPortableRules.Error(
                    Services.Gacha.Portable.GachaPortableErrorCode.InvalidPackage,
                    "The staged account manifest is incomplete.");
            }

            await VerifyEntryAsync(
                entries,
                account.UigfPath,
                account.UigfLength,
                account.UigfSha256,
                cancellationToken);
            await VerifyEntryAsync(
                entries,
                account.SupplementPath,
                account.SupplementLength,
                account.SupplementSha256,
                cancellationToken);
            expectedPaths.Add(account.UigfPath);
            expectedPaths.Add(account.SupplementPath);
            recordCount = checked(recordCount + account.RecordCount);
        }

        if (manifest.Scope.AccountCount != manifest.Accounts.Count ||
            manifest.Scope.RecordCount != recordCount)
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.RecordCountMismatch,
                "The staged manifest scope does not match its account entries.");
        }

        string[] unexpected = entries.Keys
            .Where(pathValue => !expectedPaths.Contains(pathValue))
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.UnexpectedEntry,
                $"The staged package contains unexpected entry {unexpected[0]}.");
        }
    }

    private static async Task VerifyEntryAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string path,
        long expectedLength,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out ZipArchiveEntry? entry))
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.MissingEntry,
                $"The staged package is missing {path}.",
                path);
        }

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using Stream stream = entry.Open();
        byte[] buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            length = checked(length + read);
            hash.AppendData(buffer, 0, read);
        }

        if (length != expectedLength)
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.LengthMismatch,
                $"The staged entry length does not match: {path}.",
                path);
        }

        string actualHash = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
        if (!string.Equals(
                actualHash,
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw GachaPortableRules.Error(
                Services.Gacha.Portable.GachaPortableErrorCode.HashMismatch,
                $"The staged entry hash does not match: {path}.",
                path);
        }
    }
}
