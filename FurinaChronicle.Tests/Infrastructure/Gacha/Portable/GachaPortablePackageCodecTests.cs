// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class GachaPortablePackageCodecTests
{
    [Fact]
    public async Task WriteReadAsync_RoundTripsSupportedSemantics()
    {
        GachaPortablePackage expected = CreatePackage();
        using var stream = new MemoryStream();

        GachaPortableWriteResult writeResult =
            await new GachaPortablePackageWriter().WriteAsync(stream, expected);

        Assert.Equal(1, writeResult.AccountCount);
        Assert.Equal(2, writeResult.RecordCount);
        Assert.True(writeResult.UncompressedPayloadBytes > 0);

        stream.Position = 0;
        GachaPortableReadResult result =
            await new GachaPortablePackageReader().ReadAsync(stream);

        Assert.Equal("1.0", result.FormatVersion);
        Assert.Equal(expected.GeneratedAt, result.Package.GeneratedAt);
        Assert.Equal(expected.SourceArchive, result.Package.SourceArchive);
        GachaPortableAccount actualAccount = Assert.Single(result.Package.Accounts);
        GachaPortableAccount expectedAccount = Assert.Single(expected.Accounts);
        Assert.Equal(expectedAccount.AccountReference, actualAccount.AccountReference);
        Assert.Equal(expectedAccount.RoleIdentity, actualAccount.RoleIdentity);
        Assert.Equal(expectedAccount.DisplayName, actualAccount.DisplayName);
        Assert.Equal(expectedAccount.Records.Count, actualAccount.Records.Count);

        for (int index = 0; index < expectedAccount.Records.Count; index++)
        {
            AssertRecordEqual(
                expectedAccount.Records[index],
                actualAccount.Records[index]);
        }
    }

    [Fact]
    public async Task WriteAsync_ProducesStandaloneUigfV42Payload()
    {
        GachaPortablePackage package = CreatePackage();
        using var stream = new MemoryStream();
        await new GachaPortablePackageWriter().WriteAsync(stream, package);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        string accountRef = package.Accounts[0].AccountReference
            .ToString("D")
            .ToLowerInvariant();
        ZipArchiveEntry entry = Assert.Single(
            archive.Entries,
            item => item.FullName ==
                $"accounts/{accountRef}/gacha.uigf.json");
        await using Stream payload = entry.Open();

        GachaReadResult result =
            await new UigfV42GachaReader().ReadAsync(payload);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal("100000001", Assert.Single(result.Accounts).Uid);
    }

    [Fact]
    public async Task ReadAsync_FutureMajor_ReturnsUnsupportedVersion()
    {
        MemoryStream stream = await WritePackageAsync(CreatePackage());
        MemoryStream changed = RewriteArchive(
            stream,
            entries =>
            {
                JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!
                    .AsObject();
                manifest["format_version"] = "2.0";
                entries["manifest.json"] = Encoding.UTF8.GetBytes(
                    manifest.ToJsonString());
            });

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageReader().ReadAsync(changed));

        Assert.Equal(
            GachaPortableErrorCode.UnsupportedVersion,
            exception.Code);
    }

    [Fact]
    public async Task ReadAsync_ChangedPayload_ReturnsHashMismatch()
    {
        GachaPortablePackage package = CreatePackage();
        MemoryStream stream = await WritePackageAsync(package);
        string accountRef = package.Accounts[0].AccountReference
            .ToString("D")
            .ToLowerInvariant();
        string supplementPath =
            $"accounts/{accountRef}/supplement.ndjson";
        MemoryStream changed = RewriteArchive(
            stream,
            entries =>
            {
                string supplement = Encoding.UTF8.GetString(
                    entries[supplementPath]);
                entries[supplementPath] = Encoding.UTF8.GetBytes(
                    supplement.Replace(
                        "official_api",
                        "official_apx",
                        StringComparison.Ordinal));
            });

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageReader().ReadAsync(changed));

        Assert.Equal(GachaPortableErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task ReadAsync_UnknownOrigin_ReturnsUnsupportedEnum()
    {
        GachaPortablePackage package = CreatePackage();
        MemoryStream stream = await WritePackageAsync(package);
        string accountRef = package.Accounts[0].AccountReference
            .ToString("D")
            .ToLowerInvariant();
        string supplementPath =
            $"accounts/{accountRef}/supplement.ndjson";
        MemoryStream changed = RewriteArchive(
            stream,
            entries =>
            {
                string supplement = Encoding.UTF8.GetString(
                    entries[supplementPath]);
                byte[] updated = Encoding.UTF8.GetBytes(
                    supplement.Replace(
                        "official_api",
                        "future_origin",
                        StringComparison.Ordinal));
                entries[supplementPath] = updated;
                UpdateManifestPayload(entries, supplementPath, updated);
            });

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageReader().ReadAsync(changed));

        Assert.Equal(GachaPortableErrorCode.UnsupportedEnum, exception.Code);
    }

    [Fact]
    public async Task ReadAsync_PathTraversal_ReturnsInvalidPath()
    {
        MemoryStream stream = await WritePackageAsync(CreatePackage());
        MemoryStream changed = RewriteArchive(
            stream,
            entries => entries["../outside.json"] = "{}"u8.ToArray());

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageReader().ReadAsync(changed));

        Assert.Equal(GachaPortableErrorCode.InvalidPath, exception.Code);
    }

    [Fact]
    public async Task WriteAsync_MissingRequiredItemId_ReturnsStructuredError()
    {
        GachaPortablePackage package = CreatePackage();
        GachaPortableAccount account = package.Accounts[0];
        GachaRecord invalid = account.Records[0] with { ItemId = null };
        package = package with
        {
            Accounts =
            [account with { Records = [invalid] }],
        };

        using var stream = new MemoryStream();
        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageWriter().WriteAsync(stream, package));

        Assert.Equal(
            GachaPortableErrorCode.MissingRequiredField,
            exception.Code);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableInput_ReturnsStructuredError()
    {
        MemoryStream package = await WritePackageAsync(CreatePackage());
        using var source = new NonSeekableReadStream(package.ToArray());

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => new GachaPortablePackageReader().ReadAsync(source));

        Assert.Equal(GachaPortableErrorCode.InputNotSeekable, exception.Code);
    }

    [Fact]
    public async Task WriteReadAsync_EmptyAccountIsValidSelectionWithoutCompletenessClaim()
    {
        GachaPortablePackage source = CreatePackage();
        GachaPortableAccount account = source.Accounts[0] with { Records = [] };
        source = source with { Accounts = [account] };
        using var stream = new MemoryStream();

        GachaPortableWriteResult written =
            await new GachaPortablePackageWriter().WriteAsync(stream, source);
        stream.Position = 0;
        GachaPortableReadResult read =
            await new GachaPortablePackageReader().ReadAsync(stream);

        Assert.Equal(0, written.RecordCount);
        Assert.Empty(Assert.Single(read.Package.Accounts).Records);
    }

    [Fact]
    public async Task ReadAsync_UnknownOptionalFieldsAreIgnoredWithinMajorOne()
    {
        MemoryStream stream = await WritePackageAsync(CreatePackage());
        MemoryStream changed = RewriteArchive(
            stream,
            entries =>
            {
                JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!
                    .AsObject();
                manifest["future_optional"] = "safe";
                entries["manifest.json"] = Encoding.UTF8.GetBytes(
                    manifest.ToJsonString());
            });

        GachaPortableReadResult result =
            await new GachaPortablePackageReader().ReadAsync(changed);

        Assert.Equal("1.0", result.FormatVersion);
        Assert.Equal(2, result.Package.RecordCount);
    }

    [Fact]
    public async Task WriteAsync_SameRoleGuidForDifferentNaturalIdentitiesIsRejected()
    {
        GachaPortablePackage source = CreatePackage();
        GachaPortableAccount first = source.Accounts[0];
        var otherNaturalIdentity = new GameRoleNaturalIdentity(
            "hk4e_cn",
            "cn_gf01",
            "100000002");
        var second = new GachaPortableAccount(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            new GameRoleIdentity(first.RoleIdentity.Id, otherNaturalIdentity),
            "Other",
            []);
        source = source with { Accounts = [first, second] };
        using var stream = new MemoryStream();

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(() =>
                new GachaPortablePackageWriter().WriteAsync(stream, source));

        Assert.Equal(GachaPortableErrorCode.InvalidReference, exception.Code);
    }

    private static GachaPortablePackage CreatePackage()
    {
        Guid archiveId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid accountId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var naturalIdentity = new GameRoleNaturalIdentity(
            "hk4e_cn",
            "cn_gf01",
            "100000001");
        var roleIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(
                Guid.Parse("30000000-0000-0000-0000-000000000001")),
            naturalIdentity);
        var fetchedAt = new DateTimeOffset(
            2026,
            10,
            6,
            8,
            0,
            1,
            TimeSpan.FromHours(8));
        var importedAt = new DateTimeOffset(
            2026,
            10,
            6,
            9,
            0,
            2,
            TimeSpan.FromHours(8));
        var first = new GachaRecord(
            accountId,
            "1000000000000000001",
            "测试角色",
            5,
            new DateTimeOffset(
                    2026,
                    1,
                    2,
                    3,
                    4,
                    5,
                    TimeSpan.FromHours(8))
                .AddTicks(1_234_567))
        {
            ItemId = "10000001",
            ItemType = "角色",
            GachaType = "301",
            UigfGachaType = "301",
            Count = 2,
            Provenance = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(FetchedAt: fetchedAt),
                acquisitionBatchId: new AcquisitionBatchId(
                    Guid.Parse("40000000-0000-0000-0000-000000000001"))),
        };
        var second = new GachaRecord(
            accountId,
            "1000000000000000002",
            null,
            null,
            new DateTimeOffset(
                2026,
                1,
                2,
                4,
                5,
                6,
                TimeSpan.FromHours(-5)))
        {
            ItemId = "10000002",
            GachaType = "302",
            UigfGachaType = "302",
            Provenance = new RecordProvenance(
                DataOrigin.StandardImport,
                new RecordTimestamps(ImportedAt: importedAt)),
        };

        return new GachaPortablePackage(
            new DateTimeOffset(
                2026,
                10,
                6,
                0,
                0,
                0,
                TimeSpan.Zero),
            new GachaPortableArchive(archiveId, " Portable Archive "),
            [
                new GachaPortableAccount(
                    accountId,
                    roleIdentity,
                    "测试账号",
                    [first, second]),
            ]);
    }

    private static async Task<MemoryStream> WritePackageAsync(
        GachaPortablePackage package)
    {
        var stream = new MemoryStream();
        await new GachaPortablePackageWriter().WriteAsync(stream, package);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream RewriteArchive(
        MemoryStream source,
        Action<Dictionary<string, byte[]>> change)
    {
        source.Position = 0;
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var archive = new ZipArchive(
            source,
            ZipArchiveMode.Read,
            leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream input = entry.Open();
                using var output = new MemoryStream();
                input.CopyTo(output);
                entries.Add(entry.FullName, output.ToArray());
            }
        }

        change(entries);
        var result = new MemoryStream();
        using (var archive = new ZipArchive(
            result,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream output = entry.Open();
                output.Write(content);
            }
        }

        result.Position = 0;
        return result;
    }

    private static void UpdateManifestPayload(
        IDictionary<string, byte[]> entries,
        string payloadPath,
        byte[] payload)
    {
        JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!
            .AsObject();
        JsonArray accounts = manifest["accounts"]!.AsArray();
        JsonObject account = accounts[0]!.AsObject();
        account["supplement_length"] = payload.LongLength;
        account["supplement_sha256"] = Convert.ToHexString(
                SHA256.HashData(payload))
            .ToLowerInvariant();
        entries["manifest.json"] = Encoding.UTF8.GetBytes(
            manifest.ToJsonString());
    }

    private static void AssertRecordEqual(
        GachaRecord expected,
        GachaRecord actual)
    {
        Assert.Equal(expected.GameAccountId, actual.GameAccountId);
        Assert.Equal(expected.ExternalRecordId, actual.ExternalRecordId);
        Assert.Equal(expected.ItemId, actual.ItemId);
        Assert.Equal(expected.ItemName, actual.ItemName);
        Assert.Equal(expected.ItemType, actual.ItemType);
        Assert.Equal(expected.RankType, actual.RankType);
        Assert.Equal(expected.GachaType, actual.GachaType);
        Assert.Equal(expected.UigfGachaType, actual.UigfGachaType);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Provenance, actual.Provenance);
    }

    private sealed class NonSeekableReadStream(byte[] data)
        : MemoryStream(data, writable: false)
    {
        public override bool CanSeek => false;
    }
}
