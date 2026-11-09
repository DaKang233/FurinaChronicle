// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class FileGachaPortableInputStagerTests
{
    [Fact]
    public async Task StageAsync_NonSeekableInputProducesReusableSeekableLease()
    {
        string directory = CreateDirectory();
        try
        {
            byte[] payload = RandomNumberGenerator.GetBytes(131_073);
            var stager = new FileGachaPortableInputStager(directory);
            await using IGachaPortableStagedInput staged =
                await stager.StageAsync(new NonSeekableStream(payload));

            Assert.Equal(payload.Length, staged.Length);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(payload))
                    .ToLowerInvariant(),
                staged.Sha256);
            await using Stream first = await staged.OpenReadAsync();
            await using Stream second = await staged.OpenReadAsync();
            Assert.True(first.CanSeek);
            Assert.Equal(payload, await ReadAllAsync(first));
            Assert.Equal(payload, await ReadAllAsync(second));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StageAsync_OverLimitLeavesNoTemporaryFile()
    {
        string directory = CreateDirectory();
        try
        {
            var stager = new FileGachaPortableInputStager(
                directory,
                new GachaPortableLimits { MaximumContainerBytes = 4 });

            GachaPortableException exception =
                await Assert.ThrowsAsync<GachaPortableException>(() =>
                    stager.StageAsync(new MemoryStream(new byte[5])));

            Assert.Equal(
                GachaPortableErrorCode.ResourceLimitExceeded,
                exception.Code);
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"furina-portable-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<byte[]> ReadAllAsync(Stream source)
    {
        using var destination = new MemoryStream();
        await source.CopyToAsync(destination);
        return destination.ToArray();
    }

    private sealed class NonSeekableStream(byte[] content)
        : MemoryStream(content, writable: false)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc) =>
            throw new NotSupportedException();

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }
}
